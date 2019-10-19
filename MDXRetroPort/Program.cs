using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MDXRetroPort
{
    using UpdateAction = Action<BinaryReader, BinaryWriter, int>;

    class Program
    {
        public const int SizeName = 80;
        public const int SizeFileName = 260;

        static void Main(string[] args)
        {
            if (args == null || args.Length == 0)
                return;
            if (!File.Exists(args[0]))
                return;

            ProcessFile(args[0]);
        }

        static void ProcessFile(string filename)
        {
            using (var fsIn = File.OpenRead(filename))
            using (var br = new BinaryReader(fsIn))
            using (var fsOut = new MemoryStream((int)fsIn.Length))
            using (var bw = new BinaryWriter(fsOut))
            {
                // Header
                br.BaseStream.Position += 4;
                bw.Write(1481393229);

                if (!SeekChunk(br, Chunks.VERS, out int size))
                    throw new Exception("Unable to find VERS chunk");

                uint version = br.ReadUInt32();
                if (version != 900)
                    throw new Exception($"Unexpected version. Expected 900 got {version}");

                Console.WriteLine("Processing " + filename);

                br.BaseStream.Position = 4;
                CopyChunks(br, bw);

                string filenameNew = filename.Insert(filename.LastIndexOf('.'), "_new");
                using (var fsFinal = File.Create(filenameNew))
                    fsOut.WriteTo(fsFinal);
            }
        }

        /// <summary>
        /// Seek for a specific top-level token
        /// </summary>
        /// <param name="br"></param>
        /// <param name="token"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        private static bool SeekChunk(BinaryReader br, Chunks token, out int size)
        {
            int ctoken = 0;
            size = 0;
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                ctoken = br.ReadInt32();
                size = br.ReadInt32();
                if (ctoken == (int)token)
                    return true;

                br.BaseStream.Position += size;
            }

            return false;
        }

        /// <summary>
        /// Copies chunks from one file to another
        /// </summary>
        /// <param name="br"></param>
        /// <param name="bw"></param>
        private static void CopyChunks(BinaryReader br, BinaryWriter bw)
        {
            Chunks token;
            int size;

            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                token = (Chunks)br.ReadInt32();
                size = br.ReadInt32();

                if (EditChunkAction.TryGetValue(token, out var action))
                {
                    // apply our action
                    action(br, bw, size);
                }
                else
                {
                    Console.WriteLine("Copying " + Encoding.UTF8.GetString(BitConverter.GetBytes((int)token)));

                    // copy the whole chunk as-is
                    bw.Write((int)token);
                    bw.Write(size);
                    bw.Write(br.ReadBytes(size));
                }
            }
        }


        private static void UpdateVERS(BinaryReader br, BinaryWriter bw, int size)
        {
            bw.Write((int)Chunks.VERS);
            bw.Write(4);
            bw.Write(800);
        }

        private static void UpdateGEOS(BinaryReader br, BinaryWriter bw, int size)
        {
            Console.WriteLine("'Fixing' GEOS");

            // write the header and record the chunk size offset
            bw.Write((uint)Chunks.GEOS);
            long offset = bw.BaseStream.Position;
            bw.Write(0);

            long end = br.BaseStream.Position + size;
            while (br.BaseStream.Position < end)
            {
                using (var substream = new SubStream())
                {
                    long sectionEnd = br.BaseStream.Position + br.ReadInt32();
                    substream.Write(0); // inclusive size

                    int vertexCount = 0;
                    long gndxOffset = 0;

                    int noOf = 0;
                    if (br.HasTag("VRTX"))
                    {
                        vertexCount = noOf = br.ReadInt32();
                        br.BaseStream.Position -= 8;
                        substream.Write(br.ReadBytes(8 + 12 * noOf));
                    }

                    if (br.HasTag("NRMS"))
                    {
                        noOf = br.ReadInt32();
                        br.BaseStream.Position -= 8;
                        substream.Write(br.ReadBytes(8 + 12 * noOf));
                    }

                    if (br.HasTag("PTYP"))
                    {
                        noOf = br.ReadInt32();
                        br.BaseStream.Position -= 8;
                        substream.Write(br.ReadBytes(8 + 4 * noOf));
                    }

                    if (br.HasTag("PCNT"))
                    {
                        noOf = br.ReadInt32();
                        br.BaseStream.Position -= 8;
                        substream.Write(br.ReadBytes(8 + 4 * noOf));
                    }

                    if (br.HasTag("PVTX"))
                    {
                        noOf = br.ReadInt32() / 3;
                        br.BaseStream.Position -= 8;
                        substream.Write(br.ReadBytes(8 + 6 * noOf));
                    }

                    if (br.HasTag("GNDX"))
                    {
                        noOf = br.ReadInt32();
                        substream.Write((int)Chunks.GNDX);

                        if (noOf == 0) // this has been moved to the SKIN section
                        {
                            substream.Write(vertexCount);
                            gndxOffset = substream.Position;
                            substream.Write(new byte[vertexCount]);
                        }
                        else
                        {
                            substream.Write(noOf);
                            substream.Write(br.ReadBytes(noOf));
                        }
                    }

                    if (br.HasTag("MTGC"))
                    {
                        noOf = br.ReadInt32();
                        br.BaseStream.Position -= 8;
                        substream.Write(br.ReadBytes(8 + 4 * noOf));
                    }

                    if (br.HasTag("MATS"))
                    {
                        noOf = br.ReadInt32();
                        br.BaseStream.Position -= 8;
                        substream.Write(br.ReadBytes(8 + 4 * noOf));
                    }

                    substream.Write(br.ReadBytes(12)); // MaterialId, SelectionGroup, Unselectable 

                    br.BaseStream.Position += 4 + SizeName; // skip LevelOfDetail, FilePath

                    substream.Write(br.ReadBytes(28)); // Bounds

                    noOf = br.ReadInt32(); // Extents
                    br.BaseStream.Position -= 4;
                    substream.Write(br.ReadBytes(4 + noOf * 28));

                    if (br.HasTag("TANG"))
                    {
                        noOf = br.ReadInt32();
                        br.BaseStream.Position += noOf * 16; // skip Tangents
                    }

                    if (br.HasTag("SKIN"))
                    {
                        noOf = br.ReadInt32();

                        // rebuild the GNDX from the SKIN section
                        // this is (uint32 index, uint32 weight)
                        // casting the indicies to bytes is "enough" for this                        
                        if (gndxOffset > 0)
                        {
                            byte[] buffer = br.ReadBytes(noOf);
                            substream.Position = gndxOffset;
                            for (int i = 0; i < buffer.Length; i += 8)
                                substream.WriteByte(buffer[i]);
                            substream.Position = substream.Length;
                        }
                        else
                        {
                            br.BaseStream.Position += noOf;
                        }
                    }

                    // copy anything else the chunk contains
                    int remaining = (int)(sectionEnd - br.BaseStream.Position);
                    if (remaining > 0)
                        substream.Write(br.ReadBytes(remaining));

                    // update the inclusive size and write to the filestream
                    substream.Position = 0;
                    substream.Write((int)substream.Length);
                    substream.WriteTo(bw.BaseStream);
                }
            }

            // update the chunk size
            bw.BaseStream.Position = offset;
            bw.Write((int)(bw.BaseStream.Length - offset - 4));
            bw.BaseStream.Position = bw.BaseStream.Length;
        }

        private static void UpdateMTLS(BinaryReader br, BinaryWriter bw, int size)
        {
            Console.WriteLine("'Fixing' MTLS");

            // write the header and record the chunk size offset
            bw.Write((int)Chunks.MTLS);
            long offset = bw.BaseStream.Position;
            bw.Write(0);

            long end = br.BaseStream.Position + size;
            while (br.BaseStream.Position < end)
            {
                using (var substream = new SubStream())
                {
                    substream.Write(br.ReadBytes(12)); // inclusive size, PriorityPlane Flags 

                    br.BaseStream.Position += SizeName; // skip Shader

                    // write then write LAYS
                    br.AssertTag("LAYS");
                    int layerCount = br.ReadInt32();
                    substream.Write((int)Chunks.LAYS);
                    substream.Write(layerCount);

                    for (int i = 0; i < layerCount; i++)
                    {
                        using (var layerstream = new SubStream())
                        {
                            long laysEnd = br.BaseStream.Position + br.ReadInt32();

                            br.BaseStream.Position -= 4;
                            layerstream.Write(br.ReadBytes(28)); // size, BlendMode, flags, textureid, textureanimationid, coordid, alpha

                            br.BaseStream.Position += 4; // skip EmissiveGain

                            while (br.BaseStream.Position != laysEnd)
                            {
                                long start = br.BaseStream.Position;

                                string tag = br.ReadString(4);
                                int trackCount = br.ReadInt32();
                                int interpolationType = br.ReadInt32();
                                int totalSize = 16 + (trackCount * (interpolationType <= 1 ? 8 : 16)); // calc size of the whole track

                                br.BaseStream.Position = start;
                                if (tag == "KMTE")
                                    br.BaseStream.Position += totalSize; // skip 
                                else
                                    layerstream.Write(br.ReadBytes(totalSize)); // clone track
                            }

                            // update the LAYS inclusive size
                            layerstream.Position = 0;
                            layerstream.Write((int)layerstream.Length);
                            layerstream.WriteTo(substream);
                        }
                    }

                    // update the Material inclusive size
                    substream.Position = 0;
                    substream.Write((int)substream.Length);
                    substream.WriteTo(bw.BaseStream);
                }
            }

            // update the MTLS chunk size
            bw.BaseStream.Position = offset;
            bw.Write((int)(bw.BaseStream.Length - offset - 4));
            bw.BaseStream.Position = bw.BaseStream.Length;
        }


        private static readonly Dictionary<Chunks, UpdateAction> EditChunkAction = new Dictionary<Chunks, UpdateAction>()
        {
            [Chunks.BPOS] = (br, bw, size) => br.BaseStream.Position += size, // skip
            [Chunks.FAFX] = (br, bw, size) => br.BaseStream.Position += size, // skip
            [Chunks.CORN] = (br, bw, size) => br.BaseStream.Position += size, // skip
            [Chunks.VERS] = (br, bw, size) => UpdateVERS(br, bw, size),
            [Chunks.GEOS] = (br, bw, size) => UpdateGEOS(br, bw, size),
            [Chunks.MTLS] = (br, bw, size) => UpdateMTLS(br, bw, size),
        };
    }
}
