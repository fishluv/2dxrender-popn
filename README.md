# 2dxrender-popn

Render pop'n music charts into wav files. The original 2dxrender project rendered beatmania IIDX charts and supported a few more nice features. This adds barebones pop'n support and rips everything else out.

This implementation is not complete and likely never will be. V renders excellently (included in this repo - v.wav) but everything else either crashes during the final wav mixing (program runs out of memory) or just sounds "off". My guess is the sample mixing code is very unoptimized, and hold note keysounds aren't handled properly. This repo may serve as a good starting point for someone seeking to build out a proper implementation.

Requires .NET 4.8 to build.

## 2dxrender.exe

```
cmd> 2dxrender.exe --help
2dxrender 1.0.0.0
Copyright c  2018

  -b, --input-bin            Required. Input .bin chart file

  -x, --input-2dx            Required. Input .2dx audio archive

  -v, --volume               (Default: 1.0) Render volume (1.0 = 100%)

  -o, --output               Required. Output file

  --help                     Display this help screen.

  --version                  Display version information.

```
