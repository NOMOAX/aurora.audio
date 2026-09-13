using System;

namespace Aurora.Audio
{
    internal sealed class PlaybackInfo<T> where T : notnull, IEquatable<T>
    {
        internal readonly Playback<T> Playback;

        internal readonly T SoundId;

        internal PlaybackStatus PreviousStatus;

        internal double PreviousVolume;

        internal double PreviousPosition;

        internal double PreviousNormalizedPosition;

        internal PlaybackInfo(
            Playback<T>    playback,
            T              soundId,
            PlaybackStatus previousStatus,
            double         previousVolume,
            double         previousPosition,
            double         previousNormalizedPosition)
        {
            Playback                   = playback;
            SoundId                    = soundId;
            PreviousStatus             = previousStatus;
            PreviousVolume             = previousVolume;
            PreviousPosition           = previousPosition;
            PreviousNormalizedPosition = previousNormalizedPosition;
        }
    }
}
