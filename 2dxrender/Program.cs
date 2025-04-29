using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;

using CommandLine;

namespace _2dxrender
{
    class Program
    {
        enum ChartCommands
        {
            Key = 0x0145,
            LoadSample = 0x0245,
            PlayBgSample = 0x0345,
            End = 0x0645,
            PlaySample = 0x0745,
        }

        class KeyPosition
        {
            public uint offset;
            public int sampleIdx; // This is 1-based.

            public KeyPosition(uint offset, int sampleIdx)
            {
                this.offset = offset;
                this.sampleIdx = sampleIdx;
            }
        }

        static Options options = new Options();
        static int bgSampleIdx = 1; // This is 1-based. Should always be set during 2dx parsing but default to 1 just in case.

        static List<string> getAudioSamplesFrom2dx(BinaryReader reader)
        {
            var samples = new List<string>();

            reader.BaseStream.Seek(0x14, SeekOrigin.Begin);

            var fileCount = reader.ReadUInt32();

            reader.BaseStream.Seek(0x48, SeekOrigin.Begin);

            var tempPath = Path.GetTempPath();
            for (var i = 0; i < fileCount; i++)
            {
                reader.BaseStream.Seek(0x48 + i * 4, SeekOrigin.Begin);

                var offset = reader.ReadUInt32();

                reader.BaseStream.Seek(offset, SeekOrigin.Begin);

                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "2DX9")
                {
                    Console.WriteLine("{0} Not a valid 2DX audio file @ {1:x8}", i, reader.BaseStream.Position - 4);
                    Environment.Exit(-1);
                }

                var dataOffset = reader.ReadUInt32();
                var dataSize = reader.ReadInt32();

                reader.ReadBytes(2);
                // For bg samples (there should just be one in each chart) this is 0x0000.
                // For non-bg samples this is usually 0xffff, but can also be some other nonzero value.
                var sampleType = reader.ReadInt16();

                // Bg sample is usually the first sample in the container, but not always.
                // Save the index for later.
                if (sampleType == 0)
                {
                    bgSampleIdx = i + 1;
                }

                reader.BaseStream.Seek(offset + dataOffset, SeekOrigin.Begin);

                var audioBytes = reader.ReadBytes(dataSize);
                var tempFilename = Path.Combine(tempPath, String.Format("{0:d4}.wav", i + 1));

                samples.Add(tempFilename);

                File.WriteAllBytes(tempFilename, audioBytes);
            }

            return samples;
        }

        static List<string> getAudioSamples(string inputFilename)
        {
            var samples = new List<string>();

            FileAttributes attr = File.GetAttributes(inputFilename);

            using (var reader = new BinaryReader(File.Open(inputFilename, FileMode.Open)))
            {
                samples = getAudioSamplesFrom2dx(reader);
            }

            return samples;
        }

        private static List<KeyPosition> parseChartData(BinaryReader reader, List<string> samples)
        {
            var sounds = new List<KeyPosition>();
            var loadedSampleForKey = new Dictionary<uint, int>();

            // Old chart format - each command is 8 bytes:
            //  4 bytes for offset
            //  4 bytes for data
            //
            // New chart format (used since usaneko) - each command is 12 bytes:
            //  4 bytes for offset
            //  4 bytes for data
            //  4 bytes for length (used only for hold notes, introduced in usaneko)
            //
            // Determine chart format by looking at 2nd 8 bytes (0x08-0x10).
            //
            // If value is 0, we know the chart is in the new format, because
            // the first 4 bytes is the last 4 bytes of the 1st command (length, which is 0), and
            // the second 4 bytes is the first 4 bytes of the 2nd command (offset, which is also 0).
            //
            // If the value is nonzero, we know the chart is in the old format, because
            // the first 4 bytes is the first 4 bytes of the 2nd command (offset, which is 0), and
            // the second 4 bytes is the second 4 bytes of the 2nd command (data, which is nonzero).
            reader.BaseStream.Seek(8, SeekOrigin.Begin);
            var isNewFormat = reader.ReadUInt64() == 0;

            reader.BaseStream.Seek(0, SeekOrigin.Begin);

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                // 4 bytes for timestamp/offset
                var offset = reader.ReadUInt32();

                // 2 bytes for command
                var command = reader.ReadUInt16();

                // 2 bytes for value
                var value = reader.ReadUInt16();
                var val1 = (uint)value >> 12;
                var val2 = value & 0xff;

                if (isNewFormat)
                {
                    // Next 4 bytes is command length. We don't need it so skip it.
                    reader.BaseStream.Seek(4, SeekOrigin.Current);
                }

                switch (command)
                {
                    case (ushort)ChartCommands.Key:
                        {
                            // val1 = ? (probably something related to hold notes - not needed here)
                            // val2 = key index (0-based)
                            sounds.Add(new KeyPosition(offset, loadedSampleForKey[(uint)val2]));
                        }
                        break;

                    case (ushort)ChartCommands.LoadSample:
                        {
                            // val1 = key index (0-based)
                            // val2 = sample index (1-based)
                            loadedSampleForKey[val1] = val2;
                        }
                        break;

                    case (ushort)ChartCommands.PlayBgSample:
                        {
                            sounds.Add(new KeyPosition(offset, bgSampleIdx));
                        }
                        break;

                    case (ushort)ChartCommands.PlaySample:
                        {
                            // val1 = ? (not needed here)
                            // val2 = sample index (1-based)
                            sounds.Add(new KeyPosition(offset, val2));
                        }
                        break;

                    case (ushort)ChartCommands.End:
                        {
                            sounds.Add(new KeyPosition(offset, -1));
                        }
                        break;
                }
            }

            return sounds;
        }

        private static void mixFinalAudio(string outputFilename, List<KeyPosition> sounds, List<string> samples)
        {
            var mixedSamples = new List<OffsetSampleProvider>();

            foreach (var sound in sounds)
            {
                // -1 is special END value. 0 is dummy value put at beginning of charts.
                if (sound.sampleIdx == -1 || sound.sampleIdx == 0)
                    continue;

                var audioFile = new AudioFileReader(samples[sound.sampleIdx - 1]); // Sample index is 1-based. Convert to 0-based for our sample array.
                var volSample = new VolumeSampleProvider(audioFile);

                if (volSample.WaveFormat.Channels == 1)
                {
                    volSample = new VolumeSampleProvider(volSample.ToStereo());
                }

                if (volSample.WaveFormat.SampleRate != 44100)
                {
                    // Causes pop sound at end of audio
                    volSample = new VolumeSampleProvider(
                        new WaveToSampleProvider(
                            new MediaFoundationResampler(
                               new SampleToWaveProvider(volSample),
                               WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)
                            ) {
                                ResamplerQuality = 60
                            }
                        )
                    );
                }

                volSample.Volume = options.RenderVolume;

                var sample = new OffsetSampleProvider(volSample);
                sample.DelayBy = TimeSpan.FromMilliseconds(sound.offset);

                mixedSamples.Add(sample);
            }

            var mixers = new List<MixingSampleProvider>();
            for (int i = 0; i < mixedSamples.Count; i += 128)
            {
                var arr = mixedSamples.Skip(i).Take(128).ToArray();
                mixers.Add(new MixingSampleProvider(arr));
            }

            var mixer = new MixingSampleProvider(mixers);
            WaveFileWriter.CreateWaveFile16(outputFilename, mixer);
        }

        static void parseChart(string binFilename, string outputFilename, List<string> samples)
        {
            using (BinaryReader reader = new BinaryReader(File.Open(binFilename, FileMode.Open)))
            {
                var sounds = parseChartData(reader, samples);
                mixFinalAudio(outputFilename, sounds, samples);
            }

            foreach (var sampleFilename in samples)
            {
                try
                {
                    File.Delete(sampleFilename);
                }
                catch
                {
                    Console.WriteLine("Couldn't delete temp file {0} for some reason", sampleFilename);
                }
            }
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(opts => options = opts).WithNotParsed(errors => Environment.Exit(1));

            var samples = getAudioSamples(options.InputAudio);
            parseChart(options.InputChart, options.OutputFile, samples);
        }
    }
}
