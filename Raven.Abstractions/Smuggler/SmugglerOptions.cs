//-----------------------------------------------------------------------
// <copyright file="ExportSpec.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using Raven.Abstractions.Data;
using Raven.Imports.Newtonsoft.Json;
using Raven.Imports.Newtonsoft.Json.Linq;
using Raven.Abstractions.Json;
using Raven.Json.Linq;
using System.Linq;

namespace Raven.Abstractions.Smuggler
{
    public class SmugglerOptionsBase
    {
        private int batchSize;

        public SmugglerOptionsBase()
        {
            BatchSize = 1024;
        }

        /// <summary>
        /// The number of document or attachments or indexes or transformers to load in each call to the RavenDB database.
        /// </summary>
        public int BatchSize
        {
            get { return batchSize; }
            set
            {
                if (value < 1)
                    throw new InvalidOperationException("Batch size cannot be zero or a negative number");
                batchSize = value;
            }
        }
    }

    public class SmugglerOptions : SmugglerOptionsBase
	{
		public SmugglerOptions()
		{
			Filters = new List<FilterSetting>();
			OperateOnTypes = ItemType.Indexes | ItemType.Documents | ItemType.Attachments | ItemType.Transformers;
			Timeout = 30 * 1000; // 30 seconds
			ShouldExcludeExpired = false;
			LastAttachmentEtag = LastDocsEtag = Etag.Empty;
		}

		/// <summary>
		/// The path to write to when doing an export, or where to read from when doing an import.
		/// </summary>
		public string BackupPath { get; set; }

        /// <summary>
        /// The stream to write to when doing an export, or where to read from when doing an import.
        /// </summary>
        public Stream BackupStream { get; set; }

		public List<FilterSetting> Filters { get; set; }

		public Etag LastDocsEtag { get; set; }

		public Etag LastAttachmentEtag { get; set; }
        
        public bool Incremental { get; set; }

        public string TransformScript { get; set; }

		/// <summary>
		/// Specify the types to operate on. You can specify more than one type by combining items with the OR parameter.
		/// Default is all items.
		/// Usage example: OperateOnTypes = ItemType.Indexes | ItemType.Documents | ItemType.Attachments.
		/// </summary>
		public ItemType OperateOnTypes { get; set; }

		public ItemType ItemTypeParser(string items)
		{
			if (String.IsNullOrWhiteSpace(items))
			{
				return ItemType.Documents | ItemType.Indexes | ItemType.Attachments;
			}
			return (ItemType)Enum.Parse(typeof(ItemType), items, ignoreCase: true);
		}

		/// <summary>
		/// The timeout for requests
		/// </summary>
		public int Timeout { get; set; }
		
		public virtual bool MatchFilters(RavenJToken item)
		{
			foreach (var filter in Filters)
			{
				bool matchedFilter = false;
				foreach (var tuple in item.SelectTokenWithRavenSyntaxReturningFlatStructure(filter.Path))
				{
					if (tuple == null || tuple.Item1 == null)
						continue;
					var val = tuple.Item1.Type == JTokenType.String
								? tuple.Item1.Value<string>()
								: tuple.Item1.ToString(Formatting.None);
					matchedFilter |= filter.Values.Any(value => String.Equals(val, value, StringComparison.OrdinalIgnoreCase)) ==
									 filter.ShouldMatch;
				}
				if (matchedFilter == false)
					return false;
			}
			return true;
		}

		/// <summary>
		/// Should we exclude any documents which have already expired by checking the expiration meta property created by the expiration bundle
		/// </summary>
		public bool ShouldExcludeExpired { get; set; }

		public virtual bool ExcludeExpired(RavenJToken item)
		{
			var metadata = item.Value<RavenJObject>("@metadata");

			const string RavenExpirationDate = "Raven-Expiration-Date";

			// check for expired documents and exclude them if expired
			if (metadata == null)
			{
				return false;
			}
			var property = metadata[RavenExpirationDate];
			if (property == null)
				return false;

			DateTime dateTime;
			try
			{
				dateTime = property.Value<DateTime>();
			}
			catch (FormatException)
			{
				return false;
			}

			return dateTime >= SystemTime.UtcNow;
		}
	}

    public class SmugglerBetweenOptions : SmugglerOptionsBase
    {
        public RavenConnectionStringOptions From { get; set; }

        public RavenConnectionStringOptions To { get; set; }
    }

	[Flags]
	public enum ItemType
	{
		Documents = 0x1,
		Indexes = 0x2,
		Attachments = 0x4,
		Transformers = 0x8,

        RemoveAnalyzers = 0x8000
	}

	public class FilterSetting
	{
		public string Path { get; set; }
		public List<string> Values { get; set; }
		public bool ShouldMatch { get; set; }
	}
}
