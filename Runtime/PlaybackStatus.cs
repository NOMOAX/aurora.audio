namespace Aurora.Audio
{
    /// <summary>
    /// The playback status of a sound.
    /// </summary>
    public enum PlaybackStatus
    {
        /// <summary>
        /// The playback is not playing, for one of the following reasons:
        /// <list type="bullet">
        /// <item><description>It has not started yet.</description></item>
        /// <item><description>It is stopped by <see cref="Playback{T}.Stop"/>.</description></item>
        /// <item><description>It reached the end of the sound.</description></item>
        /// </list>
        /// </summary>
        None,

        /// <summary>
        /// The playback is playing.
        /// </summary>
        Playing,

        /// <summary>
        /// The playback is paused and can resume from its current position.
        /// </summary>
        Paused
    }
}
