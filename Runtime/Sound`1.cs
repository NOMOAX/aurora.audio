using System;

namespace Aurora.Audio
{
    /// <summary>
    /// Represents loaded audio data that is ready to be played.
    /// </summary>
    /// <typeparam name="T">The type of the audio file identifier. The derived type decides what type to use.</typeparam>
    /// <remarks>A sound can be reused to create multiple <see cref="Playback{T}"/> instances, possibly playing concurrently.</remarks>
    public abstract class Sound<T> : IDisposable where T : IEquatable<T>
    {
        private bool _disposed;

        /// <summary>
        /// Gets a value that indicates whether the current instance has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed;

        private readonly T _id;

        /// <summary>
        /// Gets the identifier of this sound.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This sound has been disposed.</exception>
        public T Id
        {
            get
            {
                ThrowIfDisposed();
                return _id;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Sound{T}"/> class.
        /// </summary>
        /// <param name="id">The identifier of this sound.</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
        protected Sound(T id)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            _id = id;
        }

        /// <summary>
        /// Gets the length of this sound in seconds.
        /// </summary>
        /// <remarks>The implementing type should ensure that the value is greater than 0.</remarks>
        /// <exception cref="ObjectDisposedException">This sound has been disposed.</exception>
        public abstract double Length { get; }

        /// <summary>
        /// Finalizes an instance of the <see cref="Sound{T}"/> class.
        /// </summary>
        ~Sound()
        {
            Dispose(false);
        }

        /// <summary>
        /// Releases all resources used by the current instance of the <see cref="Sound{T}"/> class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="Sound{T}"/> class and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            _disposed = true;
        }

        /// <summary>
        /// Throws an <see cref="ObjectDisposedException"/> if this sound has been disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This sound has been disposed.</exception>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
