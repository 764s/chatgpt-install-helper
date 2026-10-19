using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace ChatGPTInstaller
{
    internal static class JsonData
    {
        public static string Serialize(object value)
        {
            return CreateSerializer().Serialize(value);
        }

        public static Dictionary<string, object> Parse(string value)
        {
            return CreateSerializer().DeserializeObject(value) as Dictionary<string, object>;
        }

        public static object Get(Dictionary<string, object> value, string key)
        {
            object result;
            return value != null && value.TryGetValue(key, out result) ? result : null;
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 };
        }
    }
}
