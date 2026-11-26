using System.Diagnostics;

namespace Felix.Core.Util;

/// <summary>
/// Vitesse de téléchargement lissée sur quelques secondes (octets/s).
/// Une moyenne depuis le début donnait des « temps restants » absurdes dès que le démarrage était lent.
/// Peut être appelée depuis plusieurs threads.
/// </summary>
public sealed class SpeedMeter
{
    private const double SmoothingSeconds = 3.0;
    private const double MinSampleSeconds = 0.25;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _lock = new();
    private double _lastTime;
    private long _lastBytes;
    private double _speed;

    public double ElapsedSeconds => _clock.Elapsed.TotalSeconds;

    /// <summary>Donne le total d'octets reçus ; renvoie la vitesse lissée actuelle.</summary>
    public double Update(long bytesDone)
    {
        lock (_lock)
        {
            var now = _clock.Elapsed.TotalSeconds;
            var dt = now - _lastTime;
            if (dt < MinSampleSeconds)
                return _speed;

            var instant = Math.Max(bytesDone - _lastBytes, 0) / dt;
            var alpha = 1 - Math.Exp(-dt / SmoothingSeconds);
            _speed = _speed <= 0 ? instant : _speed + alpha * (instant - _speed);

            _lastTime = now;
            _lastBytes = bytesDone;
            return _speed;
        }
    }
}
