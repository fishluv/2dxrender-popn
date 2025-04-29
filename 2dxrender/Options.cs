using CommandLine;

namespace _2dxrender
{
    class Options
    {
        [Option('b', "input-bin", Required = true, HelpText = "Input .bin chart file")]
        public string InputChart { get; set; }

        [Option('x', "input-2dx", Required = true, HelpText = "Input .2dx audio archive")]
        public string InputAudio { get; set; }

        [Option('v', "volume", Default = 1.0f, HelpText = "Render volume (1.0 = 100%)")]
        public float RenderVolume { get; set; }

        [Option('o', "output", Required = true, HelpText = "Output file")]
        public string OutputFile { get; set; }
    }
}
