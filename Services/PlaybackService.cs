using System.Windows.Threading;
using MyQQMusic.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MyQQMusic.Services;

/// <summary>
/// 播放引擎（NAudio WASAPI 共享模式），负责解码、播放控制与播放队列推进，
/// 支持指定音频输出设备（MMDevice）并在运行时切换。
/// </summary>
public class PlaybackService : IDisposable
{
    private IWavePlayer _out;
    private AudioFileReader? _reader;
    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();
    private bool _stopRequested;
    private bool _subscribed;
    private int _loadSeq;
    private int _decodeFailStreak; // 连续解码失败计数，用于自动跳过无法解码的歌曲
    private double _volume = 0.8;

    public PlaybackService()
    {
        _out = CreateOutput(SettingsService.Current.AudioDeviceId);
        Hook();
        // 使用 Render 优先级：播放页存在持续动画（唱片旋转、歌词滚动）时，
        // 默认 Background 优先级的计时器会被饿死，导致播放条进度不同步
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += (_, _) => ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>按设备 ID 创建 WASAPI 共享模式输出；ID 为空或设备不可用时回退系统默认设备</summary>
    private static IWavePlayer CreateOutput(string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(deviceId);
                return new WasapiOut(device, AudioClientShareMode.Shared, true, 200);
            }
            catch
            {
                // 设备可能已被拔出/禁用，回退默认输出
            }
        }
        return new WasapiOut(AudioClientShareMode.Shared, 200);
    }

    /// <summary>枚举当前可用的播放（Render）设备；首项为系统默认设备（空 ID）</summary>
    public static IReadOnlyList<(string Id, string Name)> ListOutputDevices()
    {
        var list = new List<(string Id, string Name)> { ("", "系统默认输出设备") };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string defaultId = "";
            try
            {
                defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            }
            catch { }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var name = device.FriendlyName + (device.ID == defaultId ? "（默认）" : "");
                list.Add((device.ID, name));
            }
        }
        catch { }
        return list;
    }

    /// <summary>切换音频输出设备，保持当前曲目与进度续播</summary>
    public void SetOutputDevice(string? deviceId)
    {
        var id = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        var current = string.IsNullOrWhiteSpace(SettingsService.Current.AudioDeviceId)
            ? null : SettingsService.Current.AudioDeviceId;
        if (current == id) return;

        SettingsService.Current.AudioDeviceId = id;
        SettingsService.Save();

        bool wasPlaying = IsPlaying;
        double position = Position;

        _stopRequested = true;
        _timer.Stop();
        Unhook();
        _out.Stop();
        _out.Dispose();
        _out = CreateOutput(id);
        Hook();

        if (_reader != null)
        {
            _stopRequested = false;
            _out.Init(_reader);
            try { _reader.CurrentTime = TimeSpan.FromSeconds(position); } catch { }
            if (wasPlaying)
            {
                _out.Play();
                IsPlaying = true;
                _timer.Start();
            }
            else
            {
                IsPlaying = false;
            }
        }
        RaiseState();
    }

    private IReadOnlyList<SongInfo>? _queue;
    /// <summary>当前播放队列（赋新队列时复位连续解码失败计数）</summary>
    public IReadOnlyList<SongInfo>? Queue
    {
        get => _queue;
        private set { _queue = value; _decodeFailStreak = 0; }
    }
    public int QueueIndex { get; private set; } = -1;
    public SongInfo? CurrentSong { get; private set; }
    public bool IsPlaying { get; private set; }
    public PlayMode Mode { get; set; } = PlayMode.ListLoop;

    public double Position => _reader?.CurrentTime.TotalSeconds ?? 0;
    public double Duration => _reader is { } r && r.WaveFormat.AverageBytesPerSecond > 0
        ? r.Length / (double)r.WaveFormat.AverageBytesPerSecond : 0;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_reader != null) _reader.Volume = (float)_volume;
            SettingsService.Current.Volume = _volume; // 退出时统一持久化
        }
    }

    /// <summary>播放状态/当前歌曲变化（UI 线程）</summary>
    public event EventHandler? StateChanged;
    /// <summary>播放进度推进（UI 线程）</summary>
    public event EventHandler? ProgressChanged;

    public void PlayFrom(IReadOnlyList<SongInfo> list, int index)
    {
        if (list.Count == 0) return;
        Queue = list;
        QueueIndex = Math.Clamp(index, 0, list.Count - 1);
        LoadAndPlay(list[QueueIndex]);
    }

    public void TogglePlay()
    {
        if (CurrentSong == null) return;
        if (IsPlaying)
        {
            _out.Pause();
            IsPlaying = false;
            _timer.Stop();
        }
        else
        {
            _out.Play();
            IsPlaying = true;
            _timer.Start();
        }
        RaiseState();
    }

    public void Next() => Advance(false);

    public void Previous()
    {
        if (Queue == null || Queue.Count == 0) return;
        int index = Mode == PlayMode.Shuffle
            ? _random.Next(Queue.Count)
            : (QueueIndex - 1 + Queue.Count) % Queue.Count;
        QueueIndex = index;
        LoadAndPlay(Queue[index]);
    }

    /// <summary>歌曲元数据被更新后替换队列/当前歌曲引用，保持播放状态连续</summary>
    public void UpdateSong(SongInfo updated)
    {
        if (CurrentSong == null || CurrentSong.Id != updated.Id) return;
        CurrentSong = updated;
        if (Queue is IList<SongInfo> list && QueueIndex >= 0 && QueueIndex < list.Count)
            list[QueueIndex] = updated;
        RaiseState();
    }

    public void Seek(double seconds)
    {
        if (_reader == null) return;
        _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(Duration, 0.1)));
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        _stopRequested = true;
        _timer.Stop();
        _out.Stop();
        Hook(); // 恢复可能被 LoadAndPlay 临时退订的事件订阅
        IsPlaying = false;
        RaiseState();
    }

    /// <summary>订阅停止事件（幂等）。_out 处于可用状态时应保持订阅。</summary>
    private void Hook()
    {
        if (_subscribed) return;
        _out.PlaybackStopped += OnPlaybackStopped;
        _subscribed = true;
    }

    /// <summary>
    /// 退订停止事件（幂等）。WASAPI 的 PlaybackStopped 由渲染线程异步 raise，
    /// 换曲时若不先退订，旧设备 Stop() 的迟到事件会在新歌开播后误触发自动切歌，
    /// 表现为"点击歌曲后不播放/无进度"。
    /// </summary>
    private void Unhook()
    {
        if (!_subscribed) return;
        _out.PlaybackStopped -= OnPlaybackStopped;
        _subscribed = false;
    }

    /// <summary>自然播放结束时自动推进</summary>
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopRequested || !IsPlaying) return;
        Dispatch(() => Advance(true));
    }

    private void Advance(bool auto)
    {
        if (Queue == null || Queue.Count == 0) { Stop(); return; }

        if (Mode == PlayMode.SingleLoop && auto)
        {
            LoadAndPlay(Queue[QueueIndex]);
            return;
        }

        int next;
        if (Mode == PlayMode.Shuffle)
        {
            next = _random.Next(Queue.Count);
        }
        else if (Mode == PlayMode.ListLoop)
        {
            next = (QueueIndex + 1) % Queue.Count;
        }
        else // Sequential
        {
            next = QueueIndex + 1;
            if (next >= Queue.Count)
            {
                if (auto) { Stop(); return; }
                next = 0;
            }
        }

        QueueIndex = next;
        LoadAndPlay(Queue[next]);
    }

    private void LoadAndPlay(SongInfo song)
    {
        int seq = ++_loadSeq;
        _stopRequested = true;
        _timer.Stop();
        // 先退订再停止：旧输出的 PlaybackStopped 迟到事件不会再干扰新歌
        Unhook();
        _out.Stop();
        _reader?.Dispose();
        _reader = null;
        IsPlaying = false;
        CurrentSong = song;
        SettingsService.Current.LastSongPath = song.FilePath;
        SettingsService.Save();
        RaiseState();

        Task.Run(() =>
        {
            AudioFileReader reader;
            try
            {
                reader = new AudioFileReader(song.FilePath) { Volume = (float)_volume };
            }
            catch
            {
                // 解码失败（如非标准编码参数的 FLAC）：自动跳到队列下一首，全部尝试一遍后停止
                Dispatch(() =>
                {
                    if (seq != _loadSeq) return;
                    if (Queue is { Count: > 0 } && _decodeFailStreak < Queue.Count)
                    {
                        _decodeFailStreak++;
                        Advance(true);
                    }
                    else Stop();
                });
                return;
            }

            Dispatch(() =>
            {
                if (seq != _loadSeq) { reader.Dispose(); return; }
                _reader = reader;
                _stopRequested = false;
                Hook();
                try
                {
                    _out.Init(reader);
                    _out.Play();
                }
                catch
                {
                    // 指定设备初始化/播放失败：回退系统默认设备重试一次
                    Unhook();
                    try { _out.Dispose(); } catch { }
                    _out = CreateOutput(null);
                    Hook();
                    try
                    {
                        _out.Init(reader);
                        _out.Play();
                    }
                    catch
                    {
                        Stop();
                        return;
                    }
                }
                _decodeFailStreak = 0; // 成功打开即清零失败计数
                IsPlaying = true;
                _timer.Start();
                RaiseState();
                PlayHistoryService.Record(song.Id); // 记入听歌历史，供首页推荐/热门计算
            });
        });
    }

    void RaiseState() => StateChanged?.Invoke(this, EventArgs.Empty);

    static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(action);
        else
            action();
    }

    public void Dispose()
    {
        _timer.Stop();
        Unhook();
        _out.Dispose();
        _reader?.Dispose();
    }
}
