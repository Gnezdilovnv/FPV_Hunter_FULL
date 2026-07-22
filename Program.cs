using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Threading;
using System.Windows.Forms;
using System.Data.SQLite;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using OpenCvSharp;

namespace FPV_Hunter_FULL
{
    // ============================================================
    // ПОЛНЫЙ LIBIIO ДЛЯ PLUTO+
    // ============================================================
    public static class libiio
    {
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_create_context_from_uri(string uri);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iio_context_destroy(IntPtr ctx);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_context_find_device(IntPtr ctx, string name);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_device_find_channel(IntPtr dev, string name, bool output);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iio_channel_enable(IntPtr channel);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iio_channel_disable(IntPtr channel);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_device_attr_write_double(IntPtr dev, string attr, double value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_device_attr_read_double(IntPtr dev, string attr, out double value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_device_attr_write_string(IntPtr dev, string attr, string value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_device_attr_read_string(IntPtr dev, string attr, out IntPtr value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_device_create_buffer(IntPtr dev, int count, bool cyclic);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_buffer_refill(IntPtr buffer);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_buffer_push(IntPtr buffer);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_buffer_first(IntPtr buffer, IntPtr channel);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iio_buffer_destroy(IntPtr buffer);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_context_get_attr_value(IntPtr ctx, string name);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_context_get_attrs(IntPtr ctx, out IntPtr attrs);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_device_get_attrs(IntPtr dev, out IntPtr attrs);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_device_get_channels(IntPtr dev, out IntPtr channels);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_device_get_name(IntPtr dev);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_channel_get_name(IntPtr channel);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool iio_channel_is_output(IntPtr channel);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_channel_attr_write_double(IntPtr channel, string attr, double value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_channel_attr_read_double(IntPtr channel, string attr, out double value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_channel_attr_write_string(IntPtr channel, string attr, string value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_channel_attr_read_string(IntPtr channel, string attr, out IntPtr value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_device_set_trigger(IntPtr dev, IntPtr trigger);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_device_get_trigger(IntPtr dev);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_context_set_timeout(IntPtr ctx, int timeout_ms);
    }

    // ============================================================
    // РАСШИРЕННЫЙ PLUTO SDR
    // ============================================================
    public class PlutoSDR : IDisposable
    {
        private IntPtr ctx, phy, rx, tx, rx_channel, tx_channel, buffer;
        private bool connected;
        private double sampleRate = 4e6;
        private double bandwidth = 4e6;
        private double gain = 40;
        private string gainMode = "manual";
        private double frequency = 100e6;
        private bool agcEnabled = false;
        
        public string Serial { get; private set; } = "Неизвестно";
        public string Firmware { get; private set; } = "Неизвестно";
        public string ChipModel { get; private set; } = "Неизвестно";
        public string HardwareModel { get; private set; } = "Неизвестно";
        public string Temperature { get; private set; } = "Неизвестно";
        public string Voltage { get; private set; } = "Неизвестно";
        public string Current { get; private set; } = "Неизвестно";
        public string FilterType { get; private set; } = "Неизвестно";
        public string GainTable { get; private set; } = "Неизвестно";
        
        public event Action<string> OnStatusUpdate;
        public event Action<float[]> OnIQData;
        public event Action<double> OnFrequencyChange;
        public event Action<double> OnGainChange;
        public event Action<double> OnRSSIChange;

        public bool Connect(string ip = "192.168.2.1")
        {
            Log("Подключение к Pluto+...");
            Disconnect();
            
            // Пробуем IP
            ctx = libiio.iio_create_context_from_uri($"ip:{ip}");
            if (ctx == IntPtr.Zero) 
                ctx = libiio.iio_create_context_from_uri("usb:");
            if (ctx == IntPtr.Zero) 
            { 
                Log("Pluto+ не найден!");
                return false; 
            }
            
            // Устанавливаем таймаут
            libiio.iio_context_set_timeout(ctx, 5000);
            
            // Находим устройства
            phy = libiio.iio_context_find_device(ctx, "ad9361-phy");
            rx = libiio.iio_context_find_device(ctx, "cf-ad9361-lpc");
            tx = libiio.iio_context_find_device(ctx, "cf-ad9361-dds-core-lpc");
            
            if (phy == IntPtr.Zero || rx == IntPtr.Zero) 
            { 
                libiio.iio_context_destroy(ctx); 
                ctx = IntPtr.Zero; 
                Log("Устройства AD9361 не найдены!"); 
                return false; 
            }
            
            // Настраиваем RX канал
            rx_channel = libiio.iio_device_find_channel(rx, "voltage0", false);
            if (rx_channel == IntPtr.Zero) 
            { 
                Log("RX канал не найден!"); 
                return false; 
            }
            libiio.iio_channel_enable(rx_channel);
            
            // Настраиваем TX канал
            tx_channel = libiio.iio_device_find_channel(tx, "voltage0", true);
            if (tx_channel != IntPtr.Zero)
            {
                libiio.iio_channel_enable(tx_channel);
            }
            
            connected = true;
            
            // Получаем информацию
            Serial = GetSerial();
            Firmware = GetFirmware();
            ChipModel = GetChipModel();
            HardwareModel = GetHardwareModel();
            Temperature = GetTemperature();
            Voltage = GetVoltage();
            Current = GetCurrent();
            FilterType = GetFilterType();
            GainTable = GetGainTable();
            
            // Настройка Pluto+
            ConfigurePluto();
            
            Log($"Pluto+ готов! Серийный: {Serial}, Модель: {ChipModel}");
            Log($"Температура: {Temperature}°C, Напряжение: {Voltage}V, Ток: {Current}A");
            Log($"Фильтр: {FilterType}, Таблица усиления: {GainTable}");
            
            return true;
        }

        private void ConfigurePluto()
        {
            // Настройка частоты
            SetFrequency(frequency);
            
            // Настройка частоты дискретизации
            SetSampleRate(sampleRate);
            
            // Настройка полосы
            SetBandwidth(bandwidth);
            
            // Настройка усиления
            SetGain(gain);
            
            // AGC
            SetAGC(agcEnabled);
            
            // Дополнительные настройки
            libiio.iio_device_attr_write_double(phy, "in_voltage_rf_bandwidth", bandwidth);
            libiio.iio_device_attr_write_double(phy, "out_voltage_rf_bandwidth", bandwidth);
            libiio.iio_device_attr_write_double(phy, "in_voltage_sampling_frequency", sampleRate);
            libiio.iio_device_attr_write_double(phy, "out_voltage_sampling_frequency", sampleRate);
            
            // Настройка фильтров
            libiio.iio_device_attr_write_string(phy, "in_voltage_filter_fir_en", "1");
            libiio.iio_device_attr_write_string(phy, "out_voltage_filter_fir_en", "1");
        }

        public void Disconnect()
        {
            if (buffer != IntPtr.Zero) 
            { 
                libiio.iio_buffer_destroy(buffer); 
                buffer = IntPtr.Zero; 
            }
            if (ctx != IntPtr.Zero) 
            { 
                libiio.iio_context_destroy(ctx); 
                ctx = IntPtr.Zero; 
            }
            connected = false;
            Log("Pluto+ отключен");
        }

        private string GetSerial()
        {
            if (ctx == IntPtr.Zero) return "Неизвестно";
            IntPtr p = libiio.iio_context_get_attr_value(ctx, "serial");
            return p != IntPtr.Zero ? Marshal.PtrToStringAnsi(p) : "Неизвестно";
        }

        private string GetFirmware()
        {
            if (phy == IntPtr.Zero) return "Неизвестно";
            double val = 0;
            int ret = libiio.iio_device_attr_read_double(phy, "fw_version", out val);
            return ret >= 0 ? val.ToString() : "Неизвестно";
        }

        private string GetChipModel()
        {
            if (phy == IntPtr.Zero) return "Неизвестно";
            double val = 0;
            int ret = libiio.iio_device_attr_read_double(phy, "model", out val);
            return ret >= 0 ? val.ToString() : "Неизвестно";
        }

        private string GetHardwareModel()
        {
            if (ctx == IntPtr.Zero) return "Неизвестно";
            IntPtr p = libiio.iio_context_get_attr_value(ctx, "hw_model");
            return p != IntPtr.Zero ? Marshal.PtrToStringAnsi(p) : "Неизвестно";
        }

        private string GetTemperature()
        {
            if (phy == IntPtr.Zero) return "Неизвестно";
            double val = 0;
            int ret = libiio.iio_device_attr_read_double(phy, "in_voltage_temperature", out val);
            return ret >= 0 ? val.ToString("F1") : "Неизвестно";
        }

        private string GetVoltage()
        {
            if (phy == IntPtr.Zero) return "Неизвестно";
            double val = 0;
            int ret = libiio.iio_device_attr_read_double(phy, "in_voltage_voltage", out val);
            return ret >= 0 ? val.ToString("F2") : "Неизвестно";
        }

        private string GetCurrent()
        {
            if (phy == IntPtr.Zero) return "Неизвестно";
            double val = 0;
            int ret = libiio.iio_device_attr_read_double(phy, "in_voltage_current", out val);
            return ret >= 0 ? val.ToString("F3") : "Неизвестно";
        }

        private string GetFilterType()
        {
            if (phy == IntPtr.Zero) return "Неизвестно";
            IntPtr val;
            int ret = libiio.iio_device_attr_read_string(phy, "in_voltage_filter_type", out val);
            return ret >= 0 ? Marshal.PtrToStringAnsi(val) : "Неизвестно";
        }

        private string GetGainTable()
        {
            if (phy == IntPtr.Zero) return "Неизвестно";
            IntPtr val;
            int ret = libiio.iio_device_attr_read_string(phy, "in_voltage_gain_table", out val);
            return ret >= 0 ? Marshal.PtrToStringAnsi(val) : "Неизвестно";
        }

        public bool SetFrequency(double freq)
        {
            if (!connected || phy == IntPtr.Zero) return false;
            
            frequency = freq;
            
            // Основная частота
            int ret = libiio.iio_device_attr_write_double(phy, "RX_LO_FREQ", freq);
            if (ret >= 0)
            {
                libiio.iio_device_attr_write_double(phy, "TX_LO_FREQ", freq);
                OnFrequencyChange?.Invoke(freq);
                Log($"Частота: {freq/1e6:F3} МГц");
            }
            return ret >= 0;
        }

        public bool SetSampleRate(double rate)
        {
            if (!connected || phy == IntPtr.Zero) return false;
            
            sampleRate = rate;
            
            // Устанавливаем частоту дискретизации
            int ret = libiio.iio_device_attr_write_double(phy, "RX_SAMPLING_FREQ", rate);
            if (ret >= 0)
            {
                libiio.iio_device_attr_write_double(phy, "TX_SAMPLING_FREQ", rate);
                libiio.iio_device_attr_write_double(rx, "RX_RF_BANDWIDTH", rate);
                libiio.iio_device_attr_write_double(tx, "TX_RF_BANDWIDTH", rate);
                Log($"Частота дискретизации: {rate/1e6:F2} МГц");
            }
            return ret >= 0;
        }

        public bool SetBandwidth(double bw)
        {
            if (!connected || phy == IntPtr.Zero) return false;
            
            bandwidth = bw;
            
            int ret = libiio.iio_device_attr_write_double(phy, "in_voltage_rf_bandwidth", bw);
            if (ret >= 0)
            {
                libiio.iio_device_attr_write_double(phy, "out_voltage_rf_bandwidth", bw);
                Log($"Полоса: {bw/1e6:F2} МГц");
            }
            return ret >= 0;
        }

        public bool SetGain(double gainValue)
        {
            if (!connected || phy == IntPtr.Zero) return false;
            
            gain = Math.Max(0, Math.Min(73, gainValue));
            
            int ret = libiio.iio_device_attr_write_double(phy, "RX_GAIN", gain);
            if (ret >= 0)
            {
                OnGainChange?.Invoke(gain);
                Log($"Усиление: {gain:F1} dB");
            }
            return ret >= 0;
        }

        public bool SetAGC(bool enable)
        {
            if (!connected || phy == IntPtr.Zero) return false;
            
            agcEnabled = enable;
            string mode = enable ? "fast_attack" : "manual";
            
            int ret = libiio.iio_device_attr_write_string(phy, "RX_GAIN_MODE", mode);
            if (ret >= 0)
            {
                libiio.iio_device_attr_write_string(phy, "TX_GAIN_MODE", mode);
                Log($"AGC: {(enable ? "Включен" : "Выключен")}");
            }
            return ret >= 0;
        }

        public bool SetGainMode(string mode)
        {
            if (!connected || phy == IntPtr.Zero) return false;
            
            gainMode = mode;
            int ret = libiio.iio_device_attr_write_string(phy, "in_voltage_gain_mode", mode);
            return ret >= 0;
        }

        public bool SetFilter(string filter)
        {
            if (!connected || phy == IntPtr.Zero) return false;
            
            int ret = libiio.iio_device_attr_write_string(phy, "in_voltage_filter_type", filter);
            return ret >= 0;
        }

        public bool EnableTX(bool enable)
        {
            if (!connected || tx == IntPtr.Zero || tx_channel == IntPtr.Zero) return false;
            
            if (enable)
                libiio.iio_channel_enable(tx_channel);
            else
                libiio.iio_channel_disable(tx_channel);
            
            Log($"TX: {(enable ? "Включен" : "Выключен")}");
            return true;
        }

        public bool TransmitIQ(float[] iqData)
        {
            if (!connected || tx == IntPtr.Zero || tx_channel == IntPtr.Zero || iqData == null)
                return false;
            
            try
            {
                int count = iqData.Length / 2;
                buffer = libiio.iio_device_create_buffer(tx, count, false);
                if (buffer == IntPtr.Zero) return false;
                
                IntPtr data = libiio.iio_buffer_first(buffer, tx_channel);
                if (data == IntPtr.Zero) 
                { 
                    libiio.iio_buffer_destroy(buffer); 
                    buffer = IntPtr.Zero; 
                    return false; 
                }
                
                for (int i = 0; i < count; i++)
                {
                    short i_val = (short)(iqData[i * 2] * 2048);
                    short q_val = (short)(iqData[i * 2 + 1] * 2048);
                    Marshal.WriteInt16(data, i * 4, i_val);
                    Marshal.WriteInt16(data, i * 4 + 2, q_val);
                }
                
                int ret = libiio.iio_buffer_push(buffer);
                libiio.iio_buffer_destroy(buffer);
                buffer = IntPtr.Zero;
                
                return ret >= 0;
            }
            catch
            {
                return false;
            }
        }

        public float[] ReceiveSamples(int count = 4096)
        {
            if (!connected || rx == IntPtr.Zero || rx_channel == IntPtr.Zero) return null;
            
            try
            {
                buffer = libiio.iio_device_create_buffer(rx, count, false);
                if (buffer == IntPtr.Zero) return null;
                
                int bytes = libiio.iio_buffer_refill(buffer);
                if (bytes < 0) 
                { 
                    libiio.iio_buffer_destroy(buffer); 
                    buffer = IntPtr.Zero; 
                    return null; 
                }
                
                IntPtr data = libiio.iio_buffer_first(buffer, rx_channel);
                if (data == IntPtr.Zero) 
                { 
                    libiio.iio_buffer_destroy(buffer); 
                    buffer = IntPtr.Zero; 
                    return null; 
                }
                
                int sampleCount = bytes / 4;
                float[] samples = new float[sampleCount];
                
                for (int i = 0; i < sampleCount; i++)
                {
                    short i_val = Marshal.ReadInt16(data, i * 4);
                    short q_val = Marshal.ReadInt16(data, i * 4 + 2);
                    samples[i] = (float)Math.Sqrt(i_val * i_val + q_val * q_val) / 2048.0f;
                }
                
                OnIQData?.Invoke(samples);
                
                libiio.iio_buffer_destroy(buffer);
                buffer = IntPtr.Zero;
                
                return samples;
            }
            catch
            {
                if (buffer != IntPtr.Zero)
                {
                    libiio.iio_buffer_destroy(buffer);
                    buffer = IntPtr.Zero;
                }
                return null;
            }
        }

        public float[] ReceiveComplex(int count = 4096)
        {
            if (!connected || rx == IntPtr.Zero || rx_channel == IntPtr.Zero) return null;
            
            try
            {
                buffer = libiio.iio_device_create_buffer(rx, count, false);
                if (buffer == IntPtr.Zero) return null;
                
                int bytes = libiio.iio_buffer_refill(buffer);
                if (bytes < 0) 
                { 
                    libiio.iio_buffer_destroy(buffer); 
                    buffer = IntPtr.Zero; 
                    return null; 
                }
                
                IntPtr data = libiio.iio_buffer_first(buffer, rx_channel);
                if (data == IntPtr.Zero) 
                { 
                    libiio.iio_buffer_destroy(buffer); 
                    buffer = IntPtr.Zero; 
                    return null; 
                }
                
                int sampleCount = bytes / 4;
                float[] samples = new float[sampleCount * 2];
                
                for (int i = 0; i < sampleCount; i++)
                {
                    samples[i * 2] = Marshal.ReadInt16(data, i * 4) / 2048.0f;
                    samples[i * 2 + 1] = Marshal.ReadInt16(data, i * 4 + 2) / 2048.0f;
                }
                
                OnIQData?.Invoke(samples);
                
                libiio.iio_buffer_destroy(buffer);
                buffer = IntPtr.Zero;
                
                return samples;
            }
            catch
            {
                if (buffer != IntPtr.Zero)
                {
                    libiio.iio_buffer_destroy(buffer);
                    buffer = IntPtr.Zero;
                }
                return null;
            }
        }

        public double GetRSSI()
        {
            if (!connected || phy == IntPtr.Zero) return -100;
            
            double rssi = -100;
            int ret = libiio.iio_device_attr_read_double(phy, "RX_RSSI", out rssi);
            
            if (ret >= 0)
                OnRSSIChange?.Invoke(rssi);
            
            return ret >= 0 ? rssi : -100;
        }

        public double GetTemperatureC()
        {
            if (!connected || phy == IntPtr.Zero) return 0;
            double val = 0;
            int ret = libiio.iio_device_attr_read_double(phy, "in_voltage_temperature", out val);
            return ret >= 0 ? val : 0;
        }

        public bool SaveIQ(float[] samples, string filename)
        {
            try 
            { 
                using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create))) 
                { 
                    foreach (var s in samples) 
                        writer.Write(s); 
                } 
                return true; 
            }
            catch { return false; }
        }

        public bool IsConnected => connected;
        public double SampleRate => sampleRate;
        public double Frequency => frequency;
        public double Gain => gain;
        public bool AGC => agcEnabled;
        public string GainMode => gainMode;

        private void Log(string msg) 
        { 
            OnStatusUpdate?.Invoke(msg); 
        }

        public void Dispose() 
        { 
            Disconnect(); 
        }
    }

    // ============================================================
    // РАСШИРЕННЫЙ ВИДЕО ДЕКОДЕР
    // ============================================================
    public class VideoDecoder : IDisposable
    {
        private VideoWriter writer;
        private bool isRecording;
        private string currentFile;
        private int frameCount;
        private int width = 640;
        private int height = 480;
        private int fps = 30;
        private Mat currentFrame;
        private object lockObj = new object();

        public VideoDecoder()
        {
            isRecording = false;
            frameCount = 0;
        }

        public void SetResolution(int w, int h)
        {
            width = w;
            height = h;
        }

        public void SetFPS(int fpsValue)
        {
            fps = fpsValue;
        }

        public void StartRecording(string path)
        {
            try
            {
                currentFile = path;
                writer = new VideoWriter(path, FourCC.Default, fps, new OpenCvSharp.Size(width, height));
                isRecording = true;
                frameCount = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка записи: {ex.Message}");
            }
        }

        public void StopRecording()
        {
            if (writer != null && writer.IsOpened())
            {
                writer.Release();
                writer.Dispose();
                writer = null;
            }
            isRecording = false;
        }

        public bool IsRecording => isRecording;

        public bool DecodeFrame(float[] iqData, out Mat frame)
        {
            frame = null;
            lock (lockObj)
            {
                try
                {
                    if (iqData == null || iqData.Length < 100) return false;

                    frame = new Mat(height, width, MatType.CV_8UC3);
                    
                    // Декодирование FPV сигнала (NTSC/PAL)
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int idx = (y * width + x) % (iqData.Length / 2);
                            float i = iqData[idx * 2];
                            float q = iqData[idx * 2 + 1];
                            
                            // Демодуляция FM
                            float amplitude = (float)Math.Sqrt(i * i + q * q);
                            float phase = (float)Math.Atan2(q, i);
                            
                            // Преобразование в RGB (PAL цветовая схема)
                            byte r = (byte)((amplitude * 0.5f + 0.5f) * 255);
                            byte g = (byte)(((Math.Sin(phase * 2) * 0.5f + 0.5f)) * 255);
                            byte b = (byte)(((Math.Cos(phase * 2) * 0.5f + 0.5f)) * 255);
                            
                            frame.Set(y, x, new Vec3b(r, g, b));
                        }
                    }
                    
                    if (isRecording && writer != null && writer.IsOpened())
                    {
                        writer.Write(frame);
                        frameCount++;
                    }
                    
                    currentFrame = frame.Clone();
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка декодирования: {ex.Message}");
                    return false;
                }
            }
        }

        public Mat GetCurrentFrame()
        {
            lock (lockObj)
            {
                return currentFrame?.Clone();
            }
        }

        public Bitmap MatToBitmap(Mat mat)
        {
            if (mat == null || mat.Empty()) return null;
            
            try
            {
                int w = mat.Width, h = mat.Height;
                Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, w, h), 
                    ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                IntPtr ptr = bmpData.Scan0;
                
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Vec3b pixel = mat.Get<Vec3b>(y, x);
                        int offset = (y * w + x) * 3;
                        Marshal.WriteByte(ptr, offset, pixel.Item2);
                        Marshal.WriteByte(ptr, offset + 1, pixel.Item1);
                        Marshal.WriteByte(ptr, offset + 2, pixel.Item0);
                    }
                }
                
                bmp.UnlockBits(bmpData);
                return bmp;
            }
            catch { return null; }
        }

        public void Dispose()
        {
            StopRecording();
            currentFrame?.Dispose();
        }
    }

    // ============================================================
    // РАСШИРЕННЫЙ АНАЛИЗАТОР
    // ============================================================
    public class AdvancedAnalyzer
    {
        public struct SignalParameters
        {
            public double Frequency;
            public double Power;
            public double Bandwidth;
            public double SNR;
            public double PeakFrequency;
            public double OccupiedBandwidth;
            public double CenterFrequency;
            public string Modulation;
            public string Standard;
            public string Quality;
            public bool IsDigital;
            public bool HasVideo;
            public double NoiseFloor;
            public double MaxPower;
            public double MinPower;
            public double RMS;
            public double PeakToAverage;
            public double KURT;
            public double SKEW;
        }

        public SignalParameters Analyze(float[] iqData, double sampleRate)
        {
            var result = new SignalParameters();
            
            if (iqData == null || iqData.Length < 100)
                return result;

            try
            {
                int n = iqData.Length;
                
                // Основные параметры
                double sum = 0, sumSq = 0;
                double maxVal = -1000, minVal = 1000;
                
                for (int i = 0; i < n; i++)
                {
                    double val = iqData[i];
                    sum += val;
                    sumSq += val * val;
                    if (val > maxVal) maxVal = val;
                    if (val < minVal) minVal = val;
                }
                
                double mean = sum / n;
                double rms = Math.Sqrt(sumSq / n);
                double variance = (sumSq / n) - (mean * mean);
                double stdDev = Math.Sqrt(variance);
                
                result.RMS = rms;
                result.MaxPower = maxVal;
                result.MinPower = minVal;
                result.PeakToAverage = maxVal / (rms + 1e-12);
                
                // SNR
                double noisePower = CalculateNoiseFloor(iqData);
                result.NoiseFloor = noisePower;
                result.SNR = 10 * Math.Log10((rms * rms) / (noisePower * noisePower + 1e-12));
                
                // FFT анализ
                var spectrum = ComputeFFT(iqData);
                result.PeakFrequency = FindPeakFrequency(spectrum, sampleRate);
                result.Bandwidth = EstimateBandwidth(spectrum, sampleRate);
                result.OccupiedBandwidth = EstimateOccupiedBandwidth(spectrum, sampleRate);
                result.CenterFrequency = EstimateCenterFrequency(spectrum, sampleRate);
                
                // Модуляция
                result.Modulation = DetectModulation(iqData);
                result.Standard = DetectStandard(iqData, sampleRate);
                result.IsDigital = IsDigitalSignal(iqData);
                result.HasVideo = DetectVideo(iqData);
                
                // Качество
                result.Quality = CalculateQuality(result);
                result.Power = 10 * Math.Log10(rms * rms + 1e-12);
                result.Frequency = result.CenterFrequency;
                
                // Статистика
                result.KURT = CalculateKurtosis(iqData);
                result.SKEW = CalculateSkewness(iqData);
            }
            catch { }
            
            return result;
        }

        private double[] ComputeFFT(float[] data)
        {
            int n = data.Length;
            double[] fft = new double[n];
            
            // Простое преобразование Фурье
            for (int k = 0; k < n; k++)
            {
                double sumRe = 0, sumIm = 0;
                for (int t = 0; t < n; t++)
                {
                    double angle = 2 * Math.PI * k * t / n;
                    sumRe += data[t] * Math.Cos(angle);
                    sumIm += data[t] * Math.Sin(angle);
                }
                fft[k] = Math.Sqrt(sumRe * sumRe + sumIm * sumIm);
            }
            return fft;
        }

        private double FindPeakFrequency(double[] spectrum, double sampleRate)
        {
            double maxVal = 0;
            int maxIdx = 0;
            for (int i = 0; i < spectrum.Length; i++)
            {
                if (spectrum[i] > maxVal)
                {
                    maxVal = spectrum[i];
                    maxIdx = i;
                }
            }
            return (double)maxIdx / spectrum.Length * sampleRate;
        }

        private double EstimateBandwidth(double[] spectrum, double sampleRate)
        {
            double maxVal = 0;
            double sum = 0;
            
            for (int i = 0; i < spectrum.Length; i++)
            {
                if (spectrum[i] > maxVal) maxVal = spectrum[i];
                sum += spectrum[i];
            }
            
            double threshold = maxVal * 0.3;
            int start = 0, end = spectrum.Length - 1;
            
            for (int i = 0; i < spectrum.Length; i++)
            {
                if (spectrum[i] > threshold) { start = i; break; }
            }
            
            for (int i = spectrum.Length - 1; i >= 0; i--)
            {
                if (spectrum[i] > threshold) { end = i; break; }
            }
            
            if (end > start)
                return (end - start) * sampleRate / spectrum.Length;
            
            return 0;
        }

        private double EstimateOccupiedBandwidth(double[] spectrum, double sampleRate)
        {
            // 99% occupied bandwidth
            double total = 0;
            for (int i = 0; i < spectrum.Length; i++)
                total += spectrum[i];
            
            double threshold = total * 0.99;
            double cumSum = 0;
            int start = 0, end = spectrum.Length - 1;
            
            for (int i = 0; i < spectrum.Length; i++)
            {
                cumSum += spectrum[i];
                if (cumSum >= threshold) { start = i; break; }
            }
            
            cumSum = 0;
            for (int i = spectrum.Length - 1; i >= 0; i--)
            {
                cumSum += spectrum[i];
                if (cumSum >= threshold) { end = i; break; }
            }
            
            if (end > start)
                return (end - start) * sampleRate / spectrum.Length;
            
            return 0;
        }

        private double EstimateCenterFrequency(double[] spectrum, double sampleRate)
        {
            double weightedSum = 0, total = 0;
            for (int i = 0; i < spectrum.Length; i++)
            {
                double freq = (double)i / spectrum.Length * sampleRate;
                weightedSum += freq * spectrum[i];
                total += spectrum[i];
            }
            return total > 0 ? weightedSum / total : 0;
        }

        private string DetectModulation(float[] data)
        {
            if (data == null || data.Length < 100) return "FM";
            
            // Анализ фазы
            double phaseMean = 0, phaseVar = 0;
            double amplitudeMean = 0, amplitudeVar = 0;
            
            for (int i = 1; i < data.Length; i++)
            {
                double phase = Math.Atan2(data[i], data[i-1]);
                double amplitude = Math.Sqrt(data[i]*data[i] + data[i-1]*data[i-1]);
                
                phaseMean += phase;
                amplitudeMean += amplitude;
            }
            
            phaseMean /= data.Length - 1;
            amplitudeMean /= data.Length - 1;
            
            for (int i = 1; i < data.Length; i++)
            {
                double phase = Math.Atan2(data[i], data[i-1]);
                double amplitude = Math.Sqrt(data[i]*data[i] + data[i-1]*data[i-1]);
                
                phaseVar += (phase - phaseMean) * (phase - phaseMean);
                amplitudeVar += (amplitude - amplitudeMean) * (amplitude - amplitudeMean);
            }
            
            phaseVar /= data.Length - 1;
            amplitudeVar /= data.Length - 1;
            
            double phaseStd = Math.Sqrt(phaseVar);
            double ampStd = Math.Sqrt(amplitudeVar);
            
            // Определение модуляции
            if (ampStd / (amplitudeMean + 1e-12) < 0.1 && phaseStd > 0.5)
                return "FM";
            else if (ampStd / (amplitudeMean + 1e-12) > 0.3 && phaseStd < 0.3)
                return "AM";
            else if (phaseStd < 0.1 && ampStd / (amplitudeMean + 1e-12) < 0.1)
                return "CW";
            else
                return "FM";
        }

        private string DetectStandard(float[] data, double sampleRate)
        {
            if (data == null || data.Length < 100) return "PAL";
            
            var spectrum = ComputeFFT(data);
            double peakFreq = FindPeakFrequency(spectrum, sampleRate);
            
            // PAL: ~4.43 МГц поднесущая
            if (peakFreq > 4.3e6 && peakFreq < 4.5e6)
                return "PAL";
            
            // NTSC: ~3.58 МГц поднесущая
            if (peakFreq > 3.5e6 && peakFreq < 3.7e6)
                return "NTSC";
            
            // SECAM: ~4.25 МГц и 4.4 МГц
            if (peakFreq > 4.2e6 && peakFreq < 4.5e6)
                return "SECAM";
            
            return "PAL";
        }

        private double CalculateNoiseFloor(float[] data)
        {
            // Сортировка для нахождения шумового пола
            var sorted = data.Select(x => x * x).OrderBy(x => x).ToArray();
            int n = sorted.Length;
            int noiseCount = (int)(n * 0.1); // 10% самых низких значений
            double sum = 0;
            for (int i = 0; i < noiseCount && i < n; i++)
                sum += sorted[i];
            
            return Math.Sqrt(sum / noiseCount);
        }

        private bool IsDigitalSignal(float[] data)
        {
            // Проверка на цифровую модуляцию
            if (data == null || data.Length < 100) return false;
            
            int transitions = 0;
            for (int i = 1; i < data.Length - 1; i++)
            {
                if (Math.Sign(data[i] - data[i-1]) != Math.Sign(data[i+1] - data[i]))
                    transitions++;
            }
            
            double transitionRate = (double)transitions / data.Length;
            return transitionRate > 0.2;
        }

        private bool DetectVideo(float[] data)
        {
            if (data == null || data.Length < 100) return false;
            
            // Проверка наличия видеосинхроимпульсов
            var spectrum = ComputeFFT(data);
            var sum = spectrum.Sum();
            var mean = sum / spectrum.Length;
            var stdDev = Math.Sqrt(spectrum.Select(x => (x - mean) * (x - mean)).Sum() / spectrum.Length);
            
            // Видеосигнал имеет характерные гармоники
            int harmonicCount = 0;
            for (int i = 1; i < 10; i++)
            {
                double freq = i * 15625; // Строчная частота
                int idx = (int)(freq / 4e6 * spectrum.Length);
                if (idx < spectrum.Length && spectrum[idx] > mean + stdDev)
                    harmonicCount++;
            }
            
            return harmonicCount > 2;
        }

        private string CalculateQuality(SignalParameters param)
        {
            double score = 0;
            
            // SNR
            if (param.SNR > 20) score += 30;
            else if (param.SNR > 10) score += 20;
            else if (param.SNR > 5) score += 10;
            
            // Стабильность сигнала
            if (param.PeakToAverage < 3) score += 20;
            else if (param.PeakToAverage < 5) score += 10;
            
            // Полоса
            if (param.Bandwidth > 0.5e6 && param.Bandwidth < 8e6) score += 30;
            else if (param.Bandwidth > 0) score += 15;
            
            // Модуляция
            if (param.Modulation == "FM") score += 20;
            else if (param.Modulation == "AM") score += 15;
            else score += 10;
            
            if (score > 80) return "Отличный";
            else if (score > 60) return "Хороший";
            else if (score > 40) return "Средний";
            else return "Плохой";
        }

        private double CalculateKurtosis(float[] data)
        {
            double mean = data.Average();
            double std = 0;
            foreach (var v in data) std += (v - mean) * (v - mean);
            std = Math.Sqrt(std / data.Length);
            
            double kurtosis = 0;
            foreach (var v in data)
                kurtosis += Math.Pow((v - mean) / (std + 1e-12), 4);
            kurtosis = kurtosis / data.Length - 3;
            
            return kurtosis;
        }

        private double CalculateSkewness(float[] data)
        {
            double mean = data.Average();
            double std = 0;
            foreach (var v in data) std += (v - mean) * (v - mean);
            std = Math.Sqrt(std / data.Length);
            
            double skewness = 0;
            foreach (var v in data)
                skewness += Math.Pow((v - mean) / (std + 1e-12), 3);
            skewness = skewness / data.Length;
            
            return skewness;
        }
    }

    // ============================================================
    // РАСШИРЕННЫЕ НАСТРОЙКИ
    // ============================================================
    public class AdvancedSettings
    {
        // Сканирование
        public double StartFreq { get; set; } = 70e6;
        public double StopFreq { get; set; } = 6e9;
        public double Step { get; set; } = 1e6;
        public double ScanSpeed { get; set; } = 50;
        public bool ContinuousScan { get; set; } = true;
        
        // Pluto+ параметры
        public double SampleRate { get; set; } = 4e6;
        public double Bandwidth { get; set; } = 4e6;
        public double Gain { get; set; } = 40;
        public bool AGC { get; set; } = false;
        public string GainMode { get; set; } = "manual";
        public string FilterType { get; set; } = "auto";
        public bool EnableTX { get; set; } = false;
        
        // Детекция
        public double SignalThreshold { get; set; } = -80;
        public double VideoThreshold { get; set; } = -50;
        public double MinSNR { get; set; } = 3;
        public bool AutoModulation { get; set; } = true;
        public bool AutoStandard { get; set; } = true;
        
        // Видео
        public int VideoWidth { get; set; } = 640;
        public int VideoHeight { get; set; } = 480;
        public int FPS { get; set; } = 30;
        public bool AutoRecord { get; set; } = true;
        public double RecordThreshold { get; set; } = -35;
        public double StopThreshold { get; set; } = -60;
        public int MinRecordDuration { get; set; } = 3;
        public int MaxRecordDuration { get; set; } = 300;
        
        // Сохранение
        public bool SaveVideo { get; set; } = true;
        public bool SaveIQ { get; set; } = true;
        public bool SaveSpectrum { get; set; } = true;
        public bool SaveReports { get; set; } = true;
        public string VideoFormat { get; set; } = "mp4";
        public int VideoBitrate { get; set; } = 2000;
        
        // Интерфейс
        public bool ShowSpectrum { get; set; } = true;
        public bool ShowWaterfall { get; set; } = true;
        public bool ShowFrequencies { get; set; } = true;
        public int SpectrumSize { get; set; } = 1024;
        
        // Оповещения
        public bool VoiceAlerts { get; set; } = true;
        public int VoiceVolume { get; set; } = 100;
        public bool SoundAlerts { get; set; } = true;
        public bool NotificationPopup { get; set; } = true;
        
        // Пути
        public string SavePath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
            "FPV_Captures");
            
        public string VideoPath => Path.Combine(SavePath, "видео");
        public string SnapshotPath => Path.Combine(SavePath, "снимки");
        public string IQPath => Path.Combine(SavePath, "iq_samples");
        public string ReportPath => Path.Combine(SavePath, "отчеты");
        public string DatabasePath => Path.Combine(SavePath, "история");
    }

    // ============================================================
    // ПРОДВИНУТЫЙ ДЕТЕКТОР СИГНАЛОВ
    // ============================================================
    public class SignalDetector
    {
        private AdvancedSettings settings;
        private AdvancedAnalyzer analyzer = new AdvancedAnalyzer();
        private List<SignalInfo> activeSignals = new List<SignalInfo>();
        private object lockObj = new object();

        public SignalDetector(AdvancedSettings settings)
        {
            this.settings = settings;
        }

        public event Action<SignalInfo> OnSignalDetected;
        public event Action<SignalInfo> OnSignalLost;
        public event Action<List<SignalInfo>> OnSignalsUpdated;

        public SignalInfo ProcessIQData(float[] iqData, double frequency, double sampleRate)
        {
            if (iqData == null || iqData.Length == 0) return null;

            var parameters = analyzer.Analyze(iqData, sampleRate);
            
            if (parameters.Power < settings.SignalThreshold)
                return null;

            lock (lockObj)
            {
                // Проверяем существующий сигнал
                var existing = activeSignals.FirstOrDefault(s => 
                    Math.Abs(s.Frequency - frequency) < 1e6);
                
                if (existing != null)
                {
                    existing.Power = parameters.Power;
                    existing.LastSeen = DateTime.Now;
                    existing.Count++;
                    existing.Bandwidth = parameters.Bandwidth;
                    existing.Modulation = parameters.Modulation;
                    existing.Standard = parameters.Standard;
                    existing.Details = $"SNR: {parameters.SNR:F1} dB, BW: {parameters.Bandwidth/1e6:F2} МГц";
                    
                    OnSignalsUpdated?.Invoke(activeSignals);
                    return existing;
                }
                else
                {
                    // Новый сигнал
                    var signal = new SignalInfo
                    {
                        Frequency = frequency,
                        Power = parameters.Power,
                        Bandwidth = parameters.Bandwidth,
                        Type = DetectSignalType(parameters),
                        HasVideo = parameters.HasVideo,
                        Modulation = parameters.Modulation,
                        Standard = parameters.Standard,
                        FirstSeen = DateTime.Now,
                        LastSeen = DateTime.Now,
                        Count = 1,
                        Details = $"SNR: {parameters.SNR:F1} dB, BW: {parameters.Bandwidth/1e6:F2} МГц, Q: {parameters.Quality}"
                    };
                    
                    activeSignals.Add(signal);
                    OnSignalDetected?.Invoke(signal);
                    OnSignalsUpdated?.Invoke(activeSignals);
                    
                    return signal;
                }
            }
        }

        private string DetectSignalType(AdvancedAnalyzer.SignalParameters param)
        {
            if (param.HasVideo)
            {
                if (param.Bandwidth > 10e6)
                    return "HD Video";
                else
                    return "Analog Video";
            }
            
            if (param.IsDigital)
            {
                if (param.Bandwidth < 0.5e6)
                    return "Digital CW";
                else if (param.Bandwidth < 2e6)
                    return "Digital Voice";
                else
                    return "Digital Data";
            }
            
            if (param.Modulation == "FM")
            {
                if (param.Bandwidth < 5e3)
                    return "CW";
                else if (param.Bandwidth < 15e3)
                    return "Voice FM";
                else
                    return "Wide FM";
            }
            
            if (param.Modulation == "AM")
                return "AM Voice";
            
            return "Unknown";
        }

        public List<SignalInfo> GetActiveSignals()
        {
            lock (lockObj)
            {
                return activeSignals.ToList();
            }
        }

        public void CleanOldSignals(int timeoutSeconds = 10)
        {
            lock (lockObj)
            {
                var threshold = DateTime.Now.AddSeconds(-timeoutSeconds);
                var removed = activeSignals.Where(s => s.LastSeen < threshold).ToList();
                
                foreach (var signal in removed)
                {
                    activeSignals.Remove(signal);
                    OnSignalLost?.Invoke(signal);
                }
                
                if (removed.Count > 0)
                    OnSignalsUpdated?.Invoke(activeSignals);
            }
        }

        public void Clear()
        {
            lock (lockObj)
            {
                activeSignals.Clear();
                OnSignalsUpdated?.Invoke(activeSignals);
            }
        }
    }

    // ============================================================
    // ГЛАВНАЯ ФОРМА
    // ============================================================
    public class MainForm : Form
    {
        private PlutoSDR pluto = new PlutoSDR();
        private AdvancedSettings settings = new AdvancedSettings();
        private SignalDetector detector;
        private VideoDecoder decoder = new VideoDecoder();
        private VoiceAnnouncer voice = new VoiceAnnouncer();
        private Database db;
        private List<SignalInfo> signals = new List<SignalInfo>();
        private System.Windows.Forms.Timer scanTimer, uiTimer, signalTimer;
        
        // UI компоненты
        private ListBox signalList;
        private PictureBox videoBox;
        private PictureBox spectrumBox;
        private PictureBox waterfallBox;
        private Label statusLabel;
        private Label plutoStatusLabel;
        private Label signalCountLabel;
        private Label recordingLabel;
        private Label rssiLabel;
        private Label tempLabel;
        private Label snrLabel;
        private Button recordBtn;
        private Button snapshotBtn;
        private Button settingsBtn;
        private Button fullscreenBtn;
        private Button scanBtn;
        private ProgressBar scanProgress;
        private ComboBox filterCombo;
        private DataGridView historyGrid;
        private TrackBar gainSlider;
        private Label gainLabel;
        private TrackBar freqSlider;
        private Label freqLabel;
        
        private bool isScanning = true;
        private bool isRecording = false;
        private bool isFullscreen = false;
        private Random rand = new Random();
        private double currentFreq = 100e6;
        private double currentRSSI = -100;
        private double currentSNR = 0;
        private float[] lastIQData;
        private int scanStep = 0;
        private long sampleCount = 0;

        public MainForm()
        {
            Text = "FPV HUNTER PRO v8.0 - FULL POTENTIAL";
            Size = new Size(1600, 1000);
            BackColor = Color.FromArgb(10, 10, 30);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;

            // Инициализация путей
            string docsDir = settings.SavePath;
            foreach (var dir in new[] { docsDir, settings.VideoPath, settings.SnapshotPath, 
                settings.IQPath, settings.ReportPath, settings.DatabasePath })
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }

            // Инициализация БД
            db = new Database(settings.DatabasePath);
            
            // Инициализация детектора
            detector = new SignalDetector(settings);
            detector.OnSignalDetected += OnSignalDetected;
            detector.OnSignalLost += OnSignalLost;
            detector.OnSignalsUpdated += OnSignalsUpdated;
            
            // Подключение к Pluto+
            pluto.OnStatusUpdate += (msg) => UpdateStatus(msg);
            pluto.OnFrequencyChange += (freq) => UpdateFrequency(freq);
            pluto.OnGainChange += (gain) => UpdateGain(gain);
            pluto.OnRSSIChange += (rssi) => UpdateRSSI(rssi);
            pluto.OnIQData += OnIQDataReceived;

            ConnectPluto();

            InitUI();
            InitTimers();
            UpdateStatus("Готов к работе");
            LoadHistory();
        }

        private void ConnectPluto()
        {
            // Пробуем разные IP
            string[] ips = { "192.168.2.1", "192.168.1.1", "169.254.0.1" };
            foreach (var ip in ips)
            {
                if (pluto.Connect(ip))
                {
                    UpdateStatus($"Pluto+ подключен по IP: {ip}");
                    UpdatePlutoStatus($"Pluto+: {pluto.Serial} | {pluto.ChipModel}");
                    return;
                }
            }
            
            // Пробуем USB
            if (pluto.Connect("usb:"))
            {
                UpdateStatus("Pluto+ подключен через USB");
                UpdatePlutoStatus($"Pluto+: {pluto.Serial} | {pluto.ChipModel}");
                return;
            }
            
            UpdateStatus("Pluto+ не найден! Проверьте подключение.");
            UpdatePlutoStatus("Pluto+: НЕ ПОДКЛЮЧЕН");
        }

        private void InitUI()
        {
            // Top Panel
            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.FromArgb(20, 20, 40) };
            
            Label title = new Label
            {
                Text = "FPV HUNTER PRO v8.0 - FULL POTENTIAL",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 126, 34),
                Location = new Point(10, 15),
                Size = new Size(550, 30)
            };
            topPanel.Controls.Add(title);

            // Статус Pluto
            plutoStatusLabel = new Label
            {
                Text = "Pluto+: НЕ ПОДКЛЮЧЕН",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.Red,
                Location = new Point(580, 10),
                Size = new Size(250, 20)
            };
            topPanel.Controls.Add(plutoStatusLabel);

            // Температура
            tempLabel = new Label
            {
                Text = "🌡️ --°C",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.LightBlue,
                Location = new Point(580, 30),
                Size = new Size(120, 20)
            };
            topPanel.Controls.Add(tempLabel);

            // RSSI
            rssiLabel = new Label
            {
                Text = "📶 RSSI: --- dB",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.LightGreen,
                Location = new Point(580, 50),
                Size = new Size(150, 20)
            };
            topPanel.Controls.Add(rssiLabel);

            // SNR
            snrLabel = new Label
            {
                Text = "📊 SNR: --- dB",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.Yellow,
                Location = new Point(740, 50),
                Size = new Size(150, 20)
            };
            topPanel.Controls.Add(snrLabel);

            // Статус записи
            recordingLabel = new Label
            {
                Text = "⏸ Запись не активна",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.Gray,
                Location = new Point(900, 10),
                Size = new Size(160, 25)
            };
            topPanel.Controls.Add(recordingLabel);

            // Кнопки управления
            int btnY = 10;
            int btnX = 1100;
            
            scanBtn = new Button
            {
                Text = "⏹ СТОП",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = isScanning ? Color.FromArgb(200, 50, 50) : Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(btnX, btnY),
                Size = new Size(90, 30)
            };
            scanBtn.Click += (s, e) => ToggleScan();
            topPanel.Controls.Add(scanBtn);
            btnX += 100;

            recordBtn = new Button
            {
                Text = "🔴 ЗАПИСЬ",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(btnX, btnY),
                Size = new Size(90, 30)
            };
            recordBtn.Click += (s, e) => ToggleRecording();
            topPanel.Controls.Add(recordBtn);
            btnX += 100;

            snapshotBtn = new Button
            {
                Text = "📷 СНИМОК",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(btnX, btnY),
                Size = new Size(90, 30)
            };
            snapshotBtn.Click += (s, e) => TakeSnapshot();
            topPanel.Controls.Add(snapshotBtn);
            btnX += 100;

            fullscreenBtn = new Button
            {
                Text = "⛶ ВО ВЕСЬ ЭКРАН",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 30, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(btnX, btnY),
                Size = new Size(110, 30)
            };
            fullscreenBtn.Click += (s, e) => ToggleFullscreen();
            topPanel.Controls.Add(fullscreenBtn);
            btnX += 120;

            settingsBtn = new Button
            {
                Text = "⚙️ НАСТРОЙКИ",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 30, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(btnX, btnY),
                Size = new Size(110, 30)
            };
            settingsBtn.Click += (s, e) => ShowAdvancedSettings();
            topPanel.Controls.Add(settingsBtn);

            // Слайдеры
            gainLabel = new Label
            {
                Text = "Усиление: 40 dB",
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.LightGray,
                Location = new Point(580, 70),
                Size = new Size(150, 20)
            };
            topPanel.Controls.Add(gainLabel);

            gainSlider = new TrackBar
            {
                Minimum = 0,
                Maximum = 73,
                Value = 40,
                Location = new Point(740, 65),
                Size = new Size(120, 25)
            };
            gainSlider.ValueChanged += (s, e) => 
            {
                if (pluto.IsConnected)
                {
                    pluto.SetGain(gainSlider.Value);
                    gainLabel.Text = $"Усиление: {gainSlider.Value} dB";
                }
            };
            topPanel.Controls.Add(gainSlider);

            freqLabel = new Label
            {
                Text = "100 МГц",
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.LightGray,
                Location = new Point(880, 70),
                Size = new Size(100, 20)
            };
            topPanel.Controls.Add(freqLabel);

            freqSlider = new TrackBar
            {
                Minimum = 70,
                Maximum = 6000,
                Value = 100,
                Location = new Point(980, 65),
                Size = new Size(150, 25)
            };
            freqSlider.ValueChanged += (s, e) =>
            {
                double freq = freqSlider.Value * 1e6;
                if (pluto.IsConnected)
                {
                    pluto.SetFrequency(freq);
                    freqLabel.Text = $"{freq/1e6:F0} МГц";
                }
            };
            topPanel.Controls.Add(freqSlider);

            Controls.Add(topPanel);

            // Основной разделитель
            SplitContainer mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 300,
                BackColor = Color.FromArgb(10, 10, 30)
            };
            Controls.Add(mainSplit);

            // Левая панель - Список сигналов
            Panel leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 35), Padding = new Padding(5) };
            
            Label signalTitle = new Label
            {
                Text = "📡 ОБНАРУЖЕННЫЕ СИГНАЛЫ",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 126, 34),
                Dock = DockStyle.Top,
                Height = 30
            };
            leftPanel.Controls.Add(signalTitle);

            signalList = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 10, 20),
                ForeColor = Color.White,
                Font = new Font("Consolas", 10),
                IntegralHeight = false
            };
            signalList.SelectedIndexChanged += SignalList_SelectedIndexChanged;
            leftPanel.Controls.Add(signalList);

            Panel filterPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                BackColor = Color.FromArgb(15, 15, 35),
                Padding = new Padding(3)
            };

            filterCombo = new ComboBox
            {
                Items = { "Все сигналы", "📡 Видео", "🎮 Пульты", "📶 WiFi", "📻 Радио" },
                SelectedIndex = 0,
                BackColor = Color.FromArgb(30, 30, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(55, 5),
                Size = new Size(120, 25)
            };
            filterCombo.SelectedIndexChanged += (s, e) => UpdateSignalList();
            filterPanel.Controls.Add(new Label
            {
                Text = "Фильтр:",
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 9),
                Location = new Point(5, 8),
                Size = new Size(45, 20)
            });
            filterPanel.Controls.Add(filterCombo);

            signalCountLabel = new Label
            {
                Text = "📡 Сигналов: 0",
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 9),
                Location = new Point(190, 8),
                Size = new Size(120, 20)
            };
            filterPanel.Controls.Add(signalCountLabel);

            Button clearBtn = new Button
            {
                Text = "🗑 ОЧИСТИТЬ",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                BackColor = Color.FromArgb(50, 30, 30),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(5, 35),
                Size = new Size(100, 25)
            };
            clearBtn.Click += (s, e) => { detector.Clear(); signals.Clear(); UpdateSignalList(); };
            filterPanel.Controls.Add(clearBtn);

            Button exportBtn = new Button
            {
                Text = "💾 ЭКСПОРТ",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 50, 30),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(115, 35),
                Size = new Size(100, 25)
            };
            exportBtn.Click += (s, e) => ExportSignals();
            filterPanel.Controls.Add(exportBtn);

            leftPanel.Controls.Add(filterPanel);
            mainSplit.Panel1.Controls.Add(leftPanel);

            // Правая панель - Табы
            TabControl tabs = new TabControl { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 10, 30), ForeColor = Color.White };

            // Видео таб
            TabPage videoTab = new TabPage("🎬 ВИДЕО");
            videoBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            videoTab.Controls.Add(videoBox);
            tabs.TabPages.Add(videoTab);

            // Спектр таб
            TabPage spectrumTab = new TabPage("📊 СПЕКТР");
            Panel spectrumPanel = new Panel { Dock = DockStyle.Fill };
            
            spectrumBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            spectrumBox.Paint += SpectrumBox_Paint;
            spectrumPanel.Controls.Add(spectrumBox);
            spectrumTab.Controls.Add(spectrumPanel);
            tabs.TabPages.Add(spectrumTab);

            // Водопад таб
            TabPage waterfallTab = new TabPage("🌊 ВОДОПАД");
            waterfallBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            waterfallBox.Paint += WaterfallBox_Paint;
            waterfallTab.Controls.Add(waterfallBox);
            tabs.TabPages.Add(waterfallTab);

            // История таб
            TabPage historyTab = new TabPage("📜 ИСТОРИЯ");
            historyGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 10, 20),
                ForeColor = Color.White,
                BackgroundColor = Color.FromArgb(10, 10, 20),
                GridColor = Color.FromArgb(30, 30, 50),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false
            };
            historyGrid.Columns.Add("Time", "Время");
            historyGrid.Columns.Add("Freq", "Частота");
            historyGrid.Columns.Add("Power", "Мощность");
            historyGrid.Columns.Add("SNR", "SNR");
            historyGrid.Columns.Add("Type", "Тип");
            historyGrid.Columns.Add("Mod", "Модуляция");
            historyGrid.Columns.Add("Std", "Стандарт");
            historyTab.Controls.Add(historyGrid);
            tabs.TabPages.Add(historyTab);

            // Анализ таб
            TabPage analysisTab = new TabPage("📈 АНАЛИЗ");
            RichTextBox analysisBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 10, 20),
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 10),
                ReadOnly = true
            };
            analysisTab.Controls.Add(analysisBox);
            tabs.TabPages.Add(analysisTab);

            mainSplit.Panel2.Controls.Add(tabs);

            // Нижняя панель
            Panel bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                BackColor = Color.FromArgb(20, 20, 40)
            };

            statusLabel = new Label
            {
                Text = "🔹 Ожидание...",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.LightGray,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            bottomPanel.Controls.Add(statusLabel);

            scanProgress = new ProgressBar
            {
                Dock = DockStyle.Right,
                Width = 200,
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };
            bottomPanel.Controls.Add(scanProgress);

            Controls.Add(bottomPanel);
        }

        private void SpectrumBox_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = spectrumBox.Width;
            int h = spectrumBox.Height;

            g.Clear(Color.Black);

            // Сетка
            Pen gridPen = new Pen(Color.FromArgb(30, 30, 50));
            for (int i = 0; i < 10; i++) 
            { 
                int x = i * w / 10; 
                g.DrawLine(gridPen, x, 0, x, h); 
            }
            for (int i = 0; i < 5; i++) 
            { 
                int y = i * h / 5; 
                g.DrawLine(gridPen, 0, y, w, y); 
            }

            if (signals.Count > 0)
            {
                // Рисуем спектр
                Pen spectrumPen = new Pen(Color.FromArgb(230, 126, 34), 2);
                for (int i = 0; i < w - 1; i++)
                {
                    double freq = 70 + (i / (double)w) * 5930;
                    double power = -100;
                    foreach (var s in signals)
                    {
                        double diff = Math.Abs(freq - s.Frequency / 1e6);
                        if (diff < 10)
                        {
                            double p = s.Power + 10 * Math.Exp(-diff * diff / 50);
                            if (p > power) power = p;
                        }
                    }
                    int y = h - 10 - (int)((power + 100) / 100 * (h - 20));
                    y = Math.Max(0, Math.Min(h - 10, y));
                    g.DrawLine(spectrumPen, i, y, i + 1, y);
                }

                // Маркеры сигналов
                Font markerFont = new Font("Segoe UI", 7);
                foreach (var s in signals)
                {
                    int x = (int)((s.Frequency / 1e6 - 70) / 5930 * w);
                    x = Math.Min(w - 10, Math.Max(10, x));
                    int y = h - 10 - (int)((s.Power + 100) / 100 * (h - 20));
                    y = Math.Min(h - 10, Math.Max(0, y));

                    Color markerColor = s.HasVideo ? Color.LimeGreen : Color.Orange;

                    g.DrawLine(new Pen(markerColor, 2), x, y - 30, x, y);

                    Rectangle flagRect = new Rectangle(x + 2, y - 30, 80, 16);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(200, markerColor)), flagRect);
                    g.DrawRectangle(new Pen(markerColor), flagRect);
                    string label = s.Type.Length > 8 ? s.Type.Substring(0, 8) : s.Type;
                    g.DrawString(label, markerFont, Brushes.White, x + 4, y - 28);

                    g.DrawString((s.Frequency / 1e6).ToString("F1") + " МГц", 
                        markerFont, Brushes.White, x - 20, y + 5);
                    g.DrawString(s.Power.ToString("F1") + " dB", 
                        markerFont, Brushes.LightGray, x - 20, y + 17);
                    g.DrawString(s.Modulation, 
                        markerFont, Brushes.LightGray, x + 55, y + 17);
                }

                // Текущая частота
                if (pluto.IsConnected)
                {
                    int x = (int)((pluto.Frequency / 1e6 - 70) / 5930 * w);
                    g.DrawLine(new Pen(Color.FromArgb(255, 0, 0, 255), 1), x, 0, x, h);
                    g.DrawString("▼", new Font("Segoe UI", 10), Brushes.Red, x - 5, h - 20);
                }
            }

            // Оси
            Font axisFont = new Font("Segoe UI", 8);
            g.DrawString("70", axisFont, Brushes.Gray, 0, h - 12);
            g.DrawString("1500", axisFont, Brushes.Gray, w / 4 - 15, h - 12);
            g.DrawString("3000", axisFont, Brushes.Gray, w / 2 - 15, h - 12);
            g.DrawString("4500", axisFont, Brushes.Gray, 3 * w / 4 - 15, h - 12);
            g.DrawString("6000", axisFont, Brushes.Gray, w - 30, h - 12);
            g.DrawString("МГц", axisFont, Brushes.Gray, w - 30, 5);
            g.DrawString("dB", axisFont, Brushes.Gray, 2, 5);
        }

        private void WaterfallBox_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = waterfallBox.Width;
            int h = waterfallBox.Height;

            g.Clear(Color.Black);

            // Рисуем водопад из спектра
            if (signals.Count > 0)
            {
                for (int y = 0; y < h; y++)
                {
                    double intensity = 1 - (double)y / h;
                    for (int x = 0; x < w; x++)
                    {
                        double freq = 70 + (x / (double)w) * 5930;
                        double power = -100;
                        foreach (var s in signals)
                        {
                            double diff = Math.Abs(freq - s.Frequency / 1e6);
                            if (diff < 20)
                            {
                                double p = s.Power + 20 * Math.Exp(-diff * diff / 100);
                                if (p > power) power = p;
                            }
                        }
                        power = (power + 100) / 100 * intensity;
                        if (power > 0)
                        {
                            int brightness = (int)(power * 255);
                            brightness = Math.Min(255, Math.Max(0, brightness));
                            Color color = Color.FromArgb(brightness, brightness / 2, 0);
                            g.FillRectangle(new SolidBrush(color), x, y, 1, 1);
                        }
                    }
                }
            }
        }

        private void SignalList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (signalList.SelectedIndex >= 0 && signalList.SelectedIndex < signals.Count)
            {
                var s = signals[signalList.SelectedIndex];
                UpdateStatus($"Выбран: {s.Frequency/1e6:F1} МГц | {s.Type} | {s.Power:F1} dB | {s.Modulation}");
                spectrumBox.Invalidate();
            }
        }

        private void InitTimers()
        {
            // Таймер сканирования
            scanTimer = new System.Windows.Forms.Timer { Interval = 10 };
            scanTimer.Tick += (s, e) =>
            {
                if (!isScanning || !pluto.IsConnected) return;

                scanStep++;
                double freq = settings.StartFreq + (scanStep % 100) * settings.Step;
                if (freq > settings.StopFreq) freq = settings.StartFreq;
                currentFreq = freq;

                if (scanStep % 5 == 0)
                {
                    pluto.SetFrequency(freq);
                    var samples = pluto.ReceiveSamples(settings.SpectrumSize);
                    
                    if (samples != null && samples.Length > 0)
                    {
                        lastIQData = samples;
                        sampleCount++;
                        
                        // Получаем RSSI
                        double rssi = pluto.GetRSSI();
                        currentRSSI = rssi;
                        UpdateRSSI(rssi);

                        // Детектируем сигналы
                        var signal = detector.ProcessIQData(samples, freq, settings.SampleRate);
                        
                        if (signal != null)
                        {
                            var existing = signals.FirstOrDefault(x => 
                                Math.Abs(x.Frequency - signal.Frequency) < 1e6);
                            
                            if (existing != null)
                            {
                                existing.Power = signal.Power;
                                existing.LastSeen = signal.LastSeen;
                                existing.Count++;
                            }
                            else
                            {
                                signals.Add(signal);
                                db.AddIntercept(signal.Frequency, signal.Power, signal.Type, 
                                    signal.Modulation, signal.Standard, signal.Bandwidth, 
                                    signal.HasVideo, signal.Details);
                                
                                if (settings.VoiceAlerts)
                                    voice.Say($"Обнаружено {signal.Type} на {signal.Frequency/1e6:F1} мегагерц");
                                
                                LoadHistory();
                            }
                            
                            UpdateSignalList();
                            spectrumBox.Invalidate();
                            waterfallBox.Invalidate();
                            
                            // Автозапись видео
                            if (signal.HasVideo && settings.AutoRecord && 
                                signal.Power > settings.RecordThreshold && !isRecording)
                            {
                                StartRecording();
                            }
                        }
                        
                        // Декодирование видео
                        if (signal != null && signal.HasVideo)
                        {
                            try
                            {
                                var complex = pluto.ReceiveComplex(settings.SpectrumSize * 2);
                                if (complex != null)
                                {
                                    Mat frame;
                                    if (decoder.DecodeFrame(complex, out frame))
                                    {
                                        var bmp = decoder.MatToBitmap(frame);
                                        if (bmp != null)
                                        {
                                            videoBox.Image = bmp;
                                        }
                                        frame.Dispose();
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                UpdateSignalCount(signals.Count);
                scanProgress.Visible = true;
                UpdatePlutoStatus($"Pluto+: {pluto.Serial} | {pluto.ChipModel} | {pluto.Frequency/1e6:F0} МГц");
            };
            scanTimer.Start();

            // Таймер UI
            uiTimer = new System.Windows.Forms.Timer { Interval = 500 };
            uiTimer.Tick += (s, e) =>
            {
                if (pluto.IsConnected)
                {
                    double temp = pluto.GetTemperatureC();
                    tempLabel.Text = $"🌡️ {temp:F1}°C";
                }
            };
            uiTimer.Start();

            // Таймер очистки сигналов
            signalTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            signalTimer.Tick += (s, e) =>
            {
                detector.CleanOldSignals(15);
                
                // Удаляем старые сигналы из списка
                var threshold = DateTime.Now.AddSeconds(-15);
                var oldSignals = signals.Where(x => x.LastSeen < threshold).ToList();
                foreach (var old in oldSignals)
                {
                    signals.Remove(old);
                }
                
                if (oldSignals.Count > 0)
                    UpdateSignalList();
            };
            signalTimer.Start();
        }

        private void OnSignalDetected(SignalInfo signal)
        {
            // Обработка нового сигнала
        }

        private void OnSignalLost(SignalInfo signal)
        {
            // Обработка пропавшего сигнала
            if (isRecording && signals.All(x => !x.HasVideo || x.Power < settings.StopThreshold))
            {
                StopRecording();
            }
        }

        private void OnSignalsUpdated(List<SignalInfo> updatedSignals)
        {
            // Обновление списка сигналов
        }

        private void OnIQDataReceived(float[] iqData)
        {
            // Обработка IQ данных в реальном времени
        }

        private void UpdateSignalList()
        {
            signalList.Items.Clear();
            string filter = filterCombo.SelectedItem?.ToString() ?? "Все сигналы";
            
            var filtered = signals.OrderBy(x => x.Frequency);
            
            foreach (var s in filtered)
            {
                if (filter == "📡 Видео" && !s.HasVideo) continue;
                if (filter == "🎮 Пульты" && !s.Type.Contains("Пульт")) continue;
                if (filter == "📶 WiFi" && !s.Type.Contains("WiFi")) continue;
                if (filter == "📻 Радио" && s.Type.Contains("WiFi")) continue;
                
                string icon = s.HasVideo ? "🟢" : (s.Type.Contains("Пульт") ? "🟡" : "⚪");
                string text = $"{icon} {(s.Frequency/1e6):F1} МГц | {s.Type} | {s.Power:F1} dB";
                if (s.HasVideo) text += " 📹";
                signalList.Items.Add(text);
            }
        }

        private void UpdateSignalCount(int count) 
        { 
            signalCountLabel.Text = $"📡 Сигналов: {count}"; 
        }

        private void UpdateStatus(string text) 
        { 
            if (statusLabel != null) 
                statusLabel.Text = "🔹 " + text; 
        }

        private void UpdatePlutoStatus(string text) 
        { 
            if (plutoStatusLabel != null) 
            {
                plutoStatusLabel.Text = text;
                plutoStatusLabel.ForeColor = pluto.IsConnected ? Color.LightGreen : Color.Red;
            }
        }

        private void UpdateRSSI(double rssi) 
        { 
            if (rssiLabel != null) 
                rssiLabel.Text = $"📶 RSSI: {rssi:F1} dB"; 
        }

        private void UpdateFrequency(double freq) 
        { 
            if (freqLabel != null) 
                freqLabel.Text = $"{freq/1e6:F0} МГц"; 
        }

        private void UpdateGain(double gain) 
        { 
            if (gainLabel != null) 
                gainLabel.Text = $"Усиление: {gain:F0} dB"; 
        }

        private void UpdateSNR(double snr) 
        { 
            if (snrLabel != null) 
                snrLabel.Text = $"📊 SNR: {snr:F1} dB"; 
        }

        private void ToggleScan()
        {
            isScanning = !isScanning;
            scanBtn.Text = isScanning ? "⏹ СТОП" : "▶ СКАНИРОВАТЬ";
            scanBtn.BackColor = isScanning ? Color.FromArgb(200, 50, 50) : Color.FromArgb(50, 50, 50);
            UpdateStatus(isScanning ? "Сканирование активно" : "Сканирование остановлено");
        }

        private void ToggleRecording()
        {
            if (isRecording) StopRecording();
            else StartRecording();
        }

        private void StartRecording()
        {
            if (isRecording) return;
            isRecording = true;
            recordingLabel.Text = "🔴 ЗАПИСЬ АКТИВНА";
            recordingLabel.ForeColor = Color.Red;
            recordBtn.Text = "⏹ СТОП";
            recordBtn.BackColor = Color.FromArgb(200, 50, 50);
            UpdateStatus("Запись начата");
            if (settings.VoiceAlerts) voice.Say("Запись начата");
        }

        private void StopRecording()
        {
            if (!isRecording) return;
            isRecording = false;
            recordingLabel.Text = "⏸ Запись не активна";
            recordingLabel.ForeColor = Color.Gray;
            recordBtn.Text = "🔴 ЗАПИСЬ";
            recordBtn.BackColor = Color.FromArgb(50, 50, 50);
            UpdateStatus("Запись остановлена");
            if (settings.VoiceAlerts) voice.Say("Запись остановлена");
        }

        private void TakeSnapshot()
        {
            if (videoBox.Image != null)
            {
                string file = Path.Combine(settings.SnapshotPath, 
                    $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                videoBox.Image.Save(file, ImageFormat.Png);
                UpdateStatus($"Снимок сохранён: {file}");
                if (settings.VoiceAlerts) voice.Say("Снимок сохранён");
            }
            else
            {
                UpdateStatus("Нет видео для снимка");
            }
        }

        private void ToggleFullscreen()
        {
            isFullscreen = !isFullscreen;
            if (isFullscreen)
            {
                this.WindowState = FormWindowState.Maximized;
                this.FormBorderStyle = FormBorderStyle.None;
                fullscreenBtn.Text = "⛶ ВЫЙТИ";
            }
            else
            {
                this.WindowState = FormWindowState.Normal;
                this.FormBorderStyle = FormBorderStyle.Sizable;
                fullscreenBtn.Text = "⛶ ВО ВЕСЬ ЭКРАН";
            }
        }

        private void ExportSignals()
        {
            try
            {
                string file = Path.Combine(settings.ReportPath, 
                    $"signals_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                
                using (var writer = new StreamWriter(file))
                {
                    writer.WriteLine("Частота,Мощность,Тип,Модуляция,Стандарт,Полоса,Видео,Время");
                    foreach (var s in signals)
                    {
                        writer.WriteLine($"{s.Frequency/1e6:F3},{s.Power:F1},{s.Type},{s.Modulation},{s.Standard},{s.Bandwidth/1e6:F3},{s.HasVideo},{s.FirstSeen}");
                    }
                }
                
                UpdateStatus($"Экспортировано {signals.Count} сигналов в {file}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка экспорта: {ex.Message}");
            }
        }

        private void LoadHistory()
        {
            if (historyGrid == null) return;
            try
            {
                var history = db.GetHistory(100);
                historyGrid.Rows.Clear();
                foreach (var h in history)
                {
                    historyGrid.Rows.Add(
                        h.FirstSeen.ToString("HH:mm:ss"),
                        (h.Frequency / 1e6).ToString("F1") + " МГц",
                        h.Power.ToString("F1") + " dB",
                        h.Details?.Contains("SNR") == true ? 
                            h.Details.Substring(h.Details.IndexOf("SNR:") + 4, 4) : "---",
                        h.Type,
                        h.Modulation,
                        h.Standard
                    );
                }
            }
            catch { }
        }

        private void ShowAdvancedSettings()
        {
            var dialog = new Form
            {
                Text = "⚙️ РАСШИРЕННЫЕ НАСТРОЙКИ",
                Size = new Size(800, 600),
                BackColor = Color.FromArgb(10, 10, 30),
                ForeColor = Color.White,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable
            };

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            
            int y = 10;
            var controls = new List<Control>();

            // Группа: Сканирование
            controls.Add(CreateGroup("📡 Сканирование", y));
            y += 35;
            
            controls.Add(CreateParam("Начальная частота", $"{(settings.StartFreq/1e6):F0} МГц", ref y));
            controls.Add(CreateParam("Конечная частота", $"{(settings.StopFreq/1e6):F0} МГц", ref y));
            controls.Add(CreateParam("Шаг сканирования", $"{(settings.Step/1e6):F0} МГц", ref y));
            y += 15;

            // Группа: Pluto+
            controls.Add(CreateGroup("📻 Pluto+", y));
            y += 35;
            
            controls.Add(CreateParam("Частота дискретизации", $"{(settings.SampleRate/1e6):F1} МГц", ref y));
            controls.Add(CreateParam("Полоса пропускания", $"{(settings.Bandwidth/1e6):F1} МГц", ref y));
            controls.Add(CreateParam("Усиление", $"{settings.Gain} dB", ref y));
            controls.Add(CreateParam("Режим усиления", settings.GainMode, ref y));
            y += 15;

            // Группа: Детекция
            controls.Add(CreateGroup("🎯 Детекция", y));
            y += 35;
            
            controls.Add(CreateParam("Порог сигнала", $"{settings.SignalThreshold} dB", ref y));
            controls.Add(CreateParam("Порог видео", $"{settings.VideoThreshold} dB", ref y));
            controls.Add(CreateParam("Минимальный SNR", $"{settings.MinSNR} dB", ref y));
            controls.Add(CreateParam("Автоопределение модуляции", settings.AutoModulation ? "Да" : "Нет", ref y));
            controls.Add(CreateParam("Автоопределение стандарта", settings.AutoStandard ? "Да" : "Нет", ref y));
            y += 15;

            // Группа: Видео
            controls.Add(CreateGroup("🎬 Видео", y));
            y += 35;
            
            controls.Add(CreateParam("Разрешение", $"{settings.VideoWidth}x{settings.VideoHeight}", ref y));
            controls.Add(CreateParam("FPS", $"{settings.FPS}", ref y));
            controls.Add(CreateParam("Автозапись", settings.AutoRecord ? "Да" : "Нет", ref y));
            controls.Add(CreateParam("Порог записи", $"{settings.RecordThreshold} dB", ref y));
            controls.Add(CreateParam("Порог остановки", $"{settings.StopThreshold} dB", ref y));
            y += 15;

            // Группа: Сохранение
            controls.Add(CreateGroup("💾 Сохранение", y));
            y += 35;
            
            controls.Add(CreateParam("Сохранять видео", settings.SaveVideo ? "Да" : "Нет", ref y));
            controls.Add(CreateParam("Сохранять IQ", settings.SaveIQ ? "Да" : "Нет", ref y));
            controls.Add(CreateParam("Сохранять спектр", settings.SaveSpectrum ? "Да" : "Нет", ref y));
            controls.Add(CreateParam("Формат видео", settings.VideoFormat, ref y));
            controls.Add(CreateParam("Битрейт", $"{settings.VideoBitrate} kbps", ref y));
            y += 15;

            // Группа: Оповещения
            controls.Add(CreateGroup("🔊 Оповещения", y));
            y += 35;
            
            controls.Add(CreateParam("Голосовые оповещения", settings.VoiceAlerts ? "Да" : "Нет", ref y));
            controls.Add(CreateParam("Звуковые оповещения", settings.SoundAlerts ? "Да" : "Нет", ref y));
            controls.Add(CreateParam("Уведомления", settings.NotificationPopup ? "Да" : "Нет", ref y));
            y += 15;

            // Кнопка закрытия
            var closeBtn = new Button
            {
                Text = "ЗАКРЫТЬ",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(panel.Width - 120, y + 20),
                Size = new Size(100, 35)
            };
            closeBtn.Click += (s, e) => dialog.Close();
            panel.Controls.Add(closeBtn);

            foreach (var c in controls)
                panel.Controls.Add(c);

            dialog.Controls.Add(panel);
            dialog.ShowDialog();
        }

        private Label CreateGroup(string text, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 126, 34),
                Location = new Point(10, y),
                Size = new Size(400, 30)
            };
        }

        private Panel CreateParam(string name, string value, ref int y)
        {
            var panel = new Panel
            {
                Location = new Point(20, y),
                Size = new Size(450, 25)
            };

            panel.Controls.Add(new Label
            {
                Text = name + ":",
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 9),
                Location = new Point(0, 3),
                Size = new Size(180, 20)
            });

            panel.Controls.Add(new Label
            {
                Text = value,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(185, 3),
                Size = new Size(200, 20)
            });

            y += 30;
            return panel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            isScanning = false;
            scanTimer?.Stop();
            uiTimer?.Stop();
            signalTimer?.Stop();
            decoder?.Dispose();
            pluto?.Dispose();
            base.OnFormClosing(e);
        }
    }

    // ============================================================
    // ПРОСТЫЕ КЛАССЫ
    // ============================================================
    public class VoiceAnnouncer
    {
        private SpeechSynthesizer synth;
        private bool enabled = true;

        public VoiceAnnouncer()
        {
            try { synth = new SpeechSynthesizer(); synth.Rate = 0; synth.Volume = 100; }
            catch { enabled = false; }
        }

        public void Say(string text) { if (!enabled || synth == null) return; try { synth.SpeakAsync(text); } catch { } }
        public void SetEnabled(bool enable) { enabled = enable; }
        public bool IsEnabled => enabled;
    }

    public class Database
    {
        private string connectionString;

        public Database(string path = null)
        {
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            string dbPath = Path.Combine(path, "history.db");
            connectionString = $"Data Source={dbPath};Version=3;";
            CreateTable();
        }

        private void CreateTable()
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"CREATE TABLE IF NOT EXISTS intercepts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    frequency REAL NOT NULL,
                    power REAL NOT NULL,
                    type TEXT NOT NULL,
                    modulation TEXT,
                    standard TEXT,
                    bandwidth REAL,
                    has_video INTEGER,
                    details TEXT
                )";
                using (var cmd = new SQLiteCommand(sql, conn))
                    cmd.ExecuteNonQuery();
            }
        }

        public void AddIntercept(double freq, double power, string type, string modulation, string standard, double bandwidth, bool hasVideo, string details = "")
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"INSERT INTO intercepts (timestamp, frequency, power, type, modulation, standard, bandwidth, has_video, details)
                              VALUES (@time, @freq, @power, @type, @mod, @std, @bw, @video, @details)";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@freq", freq);
                    cmd.Parameters.AddWithValue("@power", power);
                    cmd.Parameters.AddWithValue("@type", type);
                    cmd.Parameters.AddWithValue("@mod", modulation);
                    cmd.Parameters.AddWithValue("@std", standard);
                    cmd.Parameters.AddWithValue("@bw", bandwidth);
                    cmd.Parameters.AddWithValue("@video", hasVideo ? 1 : 0);
                    cmd.Parameters.AddWithValue("@details", details);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<SignalInfo> GetHistory(int limit = 100)
        {
            var result = new List<SignalInfo>();
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "SELECT * FROM intercepts ORDER BY id DESC LIMIT @limit";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@limit", limit);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new SignalInfo
                            {
                                Frequency = reader.GetDouble(2),
                                Power = reader.GetDouble(3),
                                Type = reader.GetString(4),
                                Modulation = reader.GetString(5),
                                Standard = reader.GetString(6),
                                Bandwidth = reader.GetDouble(7),
                                HasVideo = reader.GetInt32(8) == 1,
                                Details = reader.GetString(9),
                                FirstSeen = DateTime.Parse(reader.GetString(1))
                            });
                        }
                    }
                }
            }
            return result;
        }

        public void ClearHistory()
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "DELETE FROM intercepts";
                using (var cmd = new SQLiteCommand(sql, conn))
                    cmd.ExecuteNonQuery();
            }
        }
    }

    // ============================================================
    // ЗАПУСК
    // ============================================================
    public class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
                string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критическая ошибка:\n{ex.Message}\n\n{ex.StackTrace}", 
                    "FPV Hunter Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                File.WriteAllText("fatal_error.log", $"{DateTime.Now}: FATAL ERROR\n{ex}");
            }
        }
    }
}
