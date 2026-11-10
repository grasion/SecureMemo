using System;
using NAudio.Wave;

namespace SecureMemo.Services
{
    /// <summary>
    /// 마이크 녹음. 녹음 데이터는 메모리에 잠깐 머문 뒤 바로 암호화되어 메모 공간에 기록된다.
    /// </summary>
    public class AudioService : IDisposable
    {
        private readonly object _sync = new();
        private WaveInEvent? _waveIn;
        private EncryptedAudioWriter? _writer;
        private string? _currentFileName;

        /// <summary>녹음 시작. 반환값: 메모 공간 안의 녹음 파일 이름</summary>
        public string StartRecording(Vault vault, string memoId)
        {
            if (WaveInEvent.DeviceCount == 0)
                throw new Exception("마이크가 연결되어 있지 않습니다.\n마이크를 연결한 뒤 다시 시도해 주세요.");

            try
            {
                _currentFileName = Vault.NewAudioFileName(memoId);

                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(44100, 1),
                    BufferMilliseconds = 50
                };

                _writer = vault.CreateAudioWriter(_currentFileName, _waveIn.WaveFormat);
                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.StartRecording();
                return _currentFileName;
            }
            catch (Exception ex)
            {
                Cleanup();
                if (_currentFileName != null) vault.DeleteAudio(_currentFileName);
                _currentFileName = null;
                throw new Exception($"녹음 시작 실패: {ex.Message}");
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            lock (_sync)
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
        }

        /// <summary>녹음 중지. 반환값: 녹음 파일 이름</summary>
        public string? StopRecording()
        {
            try
            {
                if (_waveIn != null)
                {
                    _waveIn.StopRecording();
                    // 마지막 버퍼가 기록될 때까지 잠시 대기
                    System.Threading.Thread.Sleep(150);
                }

                Cleanup();
                var name = _currentFileName;
                _currentFileName = null;
                return name;
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
                    _waveIn.Dispose();
                    _waveIn = null;
                }
            }
            catch
            {
                // 정리 오류 무시
            }

            lock (_sync)
            {
                try
                {
                    _writer?.Dispose(); // 남은 데이터 암호화 후 닫기
                }
                catch
                {
                    // 정리 오류 무시
                }
                _writer = null;
            }
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
