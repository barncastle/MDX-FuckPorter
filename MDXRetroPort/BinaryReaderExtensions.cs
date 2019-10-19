using System;
using System.IO;
using System.Text;

namespace MDXRetroPort
{
    static class BinaryReaderExtensions
    {
        public const int SizeTag = 4;

        public static string ReadCString(this BinaryReader br, int length) => ReadString(br, length).TrimEnd('\0');

        public static string ReadString(this BinaryReader br, int length) => Encoding.UTF8.GetString(br.ReadBytes(length));

        public static void AssertTag(this BinaryReader br, string tag)
        {
            string _tag = br.ReadCString(SizeTag);
            if (_tag != tag)
                throw new Exception($"Expected '{tag}' at {br.BaseStream.Position - SizeTag} got '{_tag}'.");
        }

        public static bool HasTag(this BinaryReader br, string tag)
        {
            bool match = tag == br.ReadCString(SizeTag);
            if (!match)
                br.BaseStream.Position -= SizeTag;
            return match;
        }

        public static bool AtEnd(this BinaryReader br) => br.BaseStream.Position == br.BaseStream.Length;
    }
}
