package raven.client.document;

import raven.abstractions.data.Etag;
import raven.abstractions.data.JsonDocument;
import raven.abstractions.data.MultiLoadResult;
import raven.abstractions.exceptions.ConcurrencyException;
import raven.abstractions.json.linq.RavenJObject;
import raven.abstractions.json.linq.RavenJValue;
import raven.client.connection.IDatabaseCommands;
import raven.client.connection.RavenTransactionAccessor;
import raven.client.exceptions.ConflictException;

/**
 * Generate hilo numbers against a RavenDB document
 */
public class HiLoKeyGenerator extends HiLoKeyGeneratorBase {
  private final Object generatorLock = new Object();

  /**
   * Initializes a new instance of the {@link HiLoKeyGenerator} class.
   * @param tag
   * @param capacity
   */
  public HiLoKeyGenerator(String tag, long capacity) {
    super(tag, capacity);
  }


  /**
   * Generates the document key.
   * @param databaseCommands
   * @param convention
   * @param entity
   * @return
   */
  public String generateDocumentKey(IDatabaseCommands databaseCommands, DocumentConvention convention, Object entity) {
    return getDocumentKeyFromId(convention, nextId(databaseCommands));
  }

  /**
   * Create the next id (numeric)
   * @param commands
   * @return
   */
  public long nextId(IDatabaseCommands commands) {
    while (true) {
      RangeValue myRange = getRange();// thread safe copy
      long current = myRange.current.incrementAndGet();

      if (current <= myRange.max.longValue())
        return current;

      synchronized (generatorLock) {
        if (getRange() != myRange)
          // Lock was contended, and the max has already been changed. Just get a new id as usual.
          continue;

        setRange(getNextRange(commands));
      }
    }
  }

  private RangeValue getNextRange(IDatabaseCommands databaseCommands) {
    /*TODO
    using (new TransactionScope(TransactionScopeOption.Suppress))
    {
     */

    try (
        AutoCloseable close2 = RavenTransactionAccessor.supressExplicitRavenTransaction();
        AutoCloseable close3 = databaseCommands.forceReadFromMaster()) {

      modifyCapacityIfRequired();
      while (true) {
        try {
          long minNextMax = getRange().max.longValue();
          JsonDocument document;
          try {
            document = getDocument(databaseCommands);
          }
          catch (ConflictException e) {
            // resolving the conflict by selecting the highest number
            String[] conflictedVersionIds = e.getConflictedVersionIds();
            long highestMax = 0;
            for (String conflictedVersionId: conflictedVersionIds) {
              long currentMax = getMaxFromDocument(databaseCommands.get(conflictedVersionId), minNextMax);
              if (highestMax < currentMax) {
                highestMax = currentMax;
              }
            }
            RavenJObject data = new RavenJObject();
            data.add("Max", new RavenJValue(highestMax));
            JsonDocument doc = new JsonDocument(data, new RavenJObject(), getHiLoDocumentKey(), false, e.getEtag(), null);

            putDocument(databaseCommands, doc);

            continue;
          }

          long min, max;
          if (document == null) {
            min = minNextMax + 1;
            max = minNextMax + capacity;
            RavenJObject data = new RavenJObject();
            data.add("Max", new RavenJValue(max));
            document = new JsonDocument(data, new RavenJObject(), getHiLoDocumentKey(), null, Etag.empty(), null);
          } else {
            long oldMax = getMaxFromDocument(document, minNextMax);
            min = oldMax + 1;
            max = oldMax + capacity;

            document.getDataAsJson().add("Max", new RavenJValue(max));
          }
          putDocument(databaseCommands, document);

          return new RangeValue(min, max);
        } catch (ConcurrencyException e) {
          // expected, we need to retry
        }
      }
    } catch (Exception e) {
      throw new RuntimeException(e);
    }
  }

  private void putDocument(IDatabaseCommands databaseCommands, JsonDocument document) {
    databaseCommands.put(getHiLoDocumentKey(), document.getEtag(), document.getDataAsJson(), document.getMetadata());
  }

  private JsonDocument getDocument(IDatabaseCommands databaseCommands) {
    MultiLoadResult documents = databaseCommands.get(new String[] { getHiLoDocumentKey(), RAVEN_KEY_SERVER_PREFIX } , new String[0]);
    return handleGetDocumentResult(documents);
  }

}
