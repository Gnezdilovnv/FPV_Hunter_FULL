# FPV Hunter Pro v8.0
Full FPV Detection System with Pluto SDR

## Features
- Scan 70 MHz - 6 GHz
- Real-time FPV video decoding
- Spectrum analyzer with waterfall
- Voice alerts
- Video and IQ recording
- SNR, bandwidth, signal quality analysis

## Build
```bash
dotnet restore
dotnet build -c Release
```

## Requirements
- Windows 10/11
- .NET Framework 4.8
- Pluto SDR with libiio.dll
