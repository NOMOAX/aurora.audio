using System;

namespace Aurora.Audio
{
    /// <summary>
    /// Represents loaded audio data that is ready to be played.
    /// </summary>
    /// <typeparam name="T">The type of the audio file identifier. The derived type decides what type to use.</typeparam>
    /// <remarks>A sound can be reused to create multiple <see cref="Playback{T}"/> instances, possibly playing concurrently.</remarks>
    public interface ISound<out T> : IDisposable where T : notnull, IEquatable<T>
    {
        /// <summary>
        /// Gets a value that indicates whether the current instance has been disposed.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Gets the identifier of this sound.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This sound has been disposed.</exception>
        T Id { get; }

        /// <summary>
        /// Gets the length of this sound in seconds.
        /// </summary>
        /// <remarks>The implementing type should ensure that the value is greater than 0.</remarks>
        /// <exception cref="ObjectDisposedException">This sound has been disposed.</exception>
        double Length { get; }
    }
}
