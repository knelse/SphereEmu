using Sphere.Common.Interfaces.Utils;
using Sphere.Common.Packets;
using Sphere.Common.Types;

namespace Sphere.Services.Services.Utils
{
    /// <summary>
    /// Reads packet definitions and adds into a static library.
    /// </summary>
    public class PacketDefinitionParser : IPacketDefinitionParser
    {
        // Move to settings or wherever
        private const string Folder = "../PacketDefinitions";
        private const char Separator = '\t';

        private static readonly HashSet<string> IgnoreParts = new HashSet<string> { "__undef", "skip", "skip_1", "skip_100", "delimiter_test", "next_field", "field_length", "level_maybe" };

        /// <summary>
        /// Loads .spdp files from configured folder, parses it and create a ditionary of packet definitions.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, PacketDefinition?> Load()
        {
            if (!Directory.Exists(Folder))
            {
                return new Dictionary<string, PacketDefinition?>();
            }

            var parts = Directory.EnumerateFiles(Folder, "*.spdp", SearchOption.AllDirectories).Select(file =>
            {
                try
                {
                    var parts = ReadFile(file);
                    var definition = new PacketDefinition(Path.GetFileNameWithoutExtension(file), parts.ToDictionary());

                    return definition;
                }
                catch (Exception)
                {
                   return null;
                }
            });

            return parts.ToDictionary(x => x.Name);
        }

        private IEnumerable<KeyValuePair<string, vByte>> ReadFile(string file)
        {
            var content = File.ReadAllLines(file);
            return content.Select(str =>
            {
                var split = str.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
                var name = split[0];
                var value = split[9];

                if (IgnoreParts.Contains(name))
                {
                    name = Guid.NewGuid().ToString("N");
                }

                var bytes = Enumerable.Chunk(value, 8);

                return new KeyValuePair<string, vByte>(name, new vByte(bytes.Select(b => Convert.ToByte(new string(b), 2)).ToArray(), (ushort)value.Length));
            });
        }
    }
}
