﻿using System.Diagnostics;
using Raven.Server.Utils;
using Sparrow.Json;

namespace Raven.Server.Documents.Indexes.Static.Counters
{
    public class DynamicCounterEntry : AbstractDynamicObject
    {
        private CounterGroupItemMetadata _counterItemMetadata;

        private long _value;

        public override dynamic GetId()
        {
            if (_counterItemMetadata == null)
                return DynamicNullObject.Null;

            Debug.Assert(_counterItemMetadata.DocumentId != null, "_counterEntry.DocumentId != null");

            return _counterItemMetadata.DocumentId;
        }

        public override bool Set(object item)
        {
            _counterItemMetadata = (CounterGroupItemMetadata)item;
            _value = 0;

            var current = CurrentIndexingScope.Current;

            var value = current.Index
                .DocumentDatabase
                .DocumentsStorage
                .CountersStorage
                .GetCounterValue(current.QueryContext.Documents, _counterItemMetadata.DocumentId, _counterItemMetadata.CounterName);

            if (value == null)
                return false;

            _value = value.Value.Value;
            return true;
        }

        public dynamic DocumentId => TypeConverter.ToDynamicType(_counterItemMetadata.DocumentId);

        public LazyStringValue Name
        {
            get
            {
                return _counterItemMetadata.CounterName;
            }
        }

        public dynamic Value => _value;

        protected override bool TryGetByName(string name, out object result)
        {
            Debug.Assert(_counterItemMetadata != null, "Item cannot be null");

            result = DynamicNullObject.Null;
            return true;
        }
    }
}
