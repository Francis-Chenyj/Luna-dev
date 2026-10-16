using System;
using System.Collections.Generic;

namespace Luna {
    internal sealed class QueryService {
        readonly LibraryStore store;
        internal QueryService(LibraryStore store) {
            this.store = store;
        }
        internal Dictionary<string, object> LocalCatalog() { return store.LocalCatalog(); }
        internal Dictionary<string, object> LocalDetail(string key) {
            if (!DataFormat.ValidKey(key)) throw new ArgumentException("查询条件无效");
            store.RefreshProfile(DataFormat.ProfileOf(key));
            bool damaged; var focused = store.FocusedResult(key, out damaged);
            if (focused != null) return focused;
            var result = store.ReadLocalDetail(key, true);
            if (damaged && DataFormat.Get(result, "data") != null) {
                var cache = DataFormat.Obj(DataFormat.Json().DeserializeObject(DataFormat.Json().Serialize(result["cache"])));
                cache["isFallback"] = true; cache["warning"] = "当前英雄的更新记录不可用，已使用原有完整数据。"; result["cache"] = cache;
            }
            return result;
        }
    }
}
