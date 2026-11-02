using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace ProbablyModManager
{
    internal static class Storage
    {
        public static string Root = "";
        public static void Initialize(string root) { Root = root; System.IO.Directory.CreateDirectory(Root); }
        public static T Read<T>(string name) where T : class
        {
            try { var path = Path.Combine(Root, name); return File.Exists(path) ? JsonConvert.DeserializeObject<T>(File.ReadAllText(path), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 64 }) : null; }
            catch { return null; }
        }
        public static void Write(string name, object data) => WriteText(name, JsonConvert.SerializeObject(data, Formatting.Indented));
        public static void WriteText(string name, string text)
        {
            var path = Path.Combine(Root, name); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }
}
