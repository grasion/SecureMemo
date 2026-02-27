using System;
using System.IO;
using NAudio.Wave;

namespace SecureMemo.Services
{
    public class AudioService
    {
        private WaveInEvent? _waveIn;
        private WaveFileWriter? _writer;
        private string? _currentFilePath;

        public void StartRecording(string memoId)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var audioPath = Path.Combine(appData, "SecureMemo", "audio");
                Directory.CreateDirectory(audioPath);

                _currentFilePath = Path.Combine(audioPath, $"{memoId}_{DateTime.Now.Ticks}.wav");

                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(44100, 1),
                    BufferMilliseconds = 50
                };

                _writer = new WaveFileWriter(_currentFilePath, _waveIn.WaveFormat);

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;

                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                Cleanup();
                throw new Exception($"녹음 시작 실패: {ex.Message}");
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            }
            catch
            {
                // 쓰기 오류 무시
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            Cleanup();
        }

        public string? StopRecording()
        {
            try
            {
                if (_waveIn != null)
                {
                    _waveIn.StopRecording();
                    
                    // 녹음이 완전히 멈출 때까지 대기
                    System.Threading.Thread.Sleep(100);
                }

                Cleanup();

                var filePath = _currentFilePath;
                _currentFilePath = null;
                return filePath;
            }
            catch (Exception ex)
            {
                Cleanup();
                throw new Exception($"녹음 중지 실패: {ex.Message}");
            }
        }

        private void Cleanup()
        {
            try
            {
                if (_waveIn != null)
                {
                    _waveIn.DataAvailable -= OnDataAvailable;
                    _waveIn.RecordingStopped -= OnRecordingStopped;
                    _waveIn.Dispose();
                    _waveIn = null;
                }

                if (_writer != null)
                {
                    _writer.Dispose();
                    _writer = null;
                }
            }
            catch
            {
                // 정리 오류 무시
            }
        }

        public void DeleteAudio(string audioPath)
        {
            try
            {
                if (File.Exists(audioPath))
                    File.Delete(audioPath);
            }
            catch
            {
                // 삭제 실패 무시
            }
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
