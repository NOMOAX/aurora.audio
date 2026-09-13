using System;

namespace Aurora.Audio
{
    /// <summary>
    /// A single independent playback of a sound.
    /// </summary>
    /// <typeparam name="T">The type of the audio file identifier. The derived type decides what type to use.</typeparam>
    /// <remarks>
    /// <para>
    /// It is created from and depends on a particular <see cref="ISound{T}"/>; a sound can be reused to create multiple playbacks.
    /// </para>
    /// <para>
    /// <see cref="Stop"/> provides a default implementation, but the derived type must still ensure that <see cref="Status"/> becomes <see cref="PlaybackStatus.None"/> after it is called.
    /// </para>
    /// </remarks>
    public abstract class Playback<T> : IDisposable where T : notnull, IEquatable<T>
    {
        private bool _disposed;

        /// <summary>
        /// Gets a value that indicates whether the current instance has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed;

        internal int InternalId;

        /// <summary>
        /// Gets a number that distinguishes this playback from every other playback of the same <see cref="AudioManager{T}"/>.
        /// </summary>
        /// <remarks>The <see cref="AudioManager{T}"/> assigns this value when the playback is created.</remarks>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        public int Id
        {
            get
            {
                ThrowIfDisposed();
                return InternalId;
            }
        }

        internal ISound<T> InternalSound;

        /// <summary>
        /// Gets the sound this playback was created from.
        /// </summary>
        /// <remarks>The <see cref="AudioManager{T}"/> assigns this value when the playback is created.</remarks>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        public ISound<T> Sound
        {
            get
            {
                ThrowIfDisposed();
                return InternalSound;
            }
        }

        internal bool InternalAutoDisposeWhenStopped;

        /// <summary>
        /// Gets a value indicating whether this playback is automatically disposed by the <see cref="AudioManager{T}"/> once it stops.
        /// </summary>
        /// <remarks>Only <see cref="AudioManager{T}"/> sets this value, when the playback is created by <see cref="AudioManager{T}.BeginPlayAndForget"/>.</remarks>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        public bool AutoDisposeWhenStopped
        {
            get
            {
                ThrowIfDisposed();
                return InternalAutoDisposeWhenStopped;
            }
        }

        /// <summary>
        /// Gets the status of this playback.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        public abstract PlaybackStatus Status { get; }

        /// <summary>
        /// Raised when the status of this playback changes.
        /// </summary>
        public virtual event Action<Playback<T>, PlaybackStatus> StatusChanged;

        /// <summary>
        /// Raises the <see cref="StatusChanged"/> event with the specified status.
        /// </summary>
        /// <param name="status">The status to raise the event with.</param>
        /// <remarks>Only <see cref="AudioManager{T}.Update"/> calls this method.</remarks>
        internal void OnStatusChanged(PlaybackStatus status)
        {
            StatusChanged?.Invoke(this, status);
        }

        /// <summary>
        /// Gets or sets the volume of this playback, in the range 0 to 1.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        public abstract double Volume { get; set; }

        /// <summary>
        /// Raised when the volume of this playback changes.
        /// </summary>
        public virtual event Action<Playback<T>, double> VolumeChanged;

        /// <summary>
        /// Raises the <see cref="VolumeChanged"/> event with the specified volume.
        /// </summary>
        /// <param name="volume">The volume to raise the event with.</param>
        /// <remarks>Only <see cref="AudioManager{T}.Update"/> calls this method.</remarks>
        internal void OnVolumeChanged(double volume)
        {
            VolumeChanged?.Invoke(this, volume);
        }

        /// <summary>
        /// Gets or sets the position of this playback in seconds.
        /// </summary>
        /// <remarks>The derived type should ensure that the value is in the range 0 to the length of <see cref="Sound"/>.</remarks>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        public abstract double Position { get; set; }

        /// <summary>
        /// Raised when the position of this playback changes.
        /// </summary>
        public virtual event Action<Playback<T>, double> PositionChanged;

        /// <summary>
        /// Raises the <see cref="PositionChanged"/> event with the specified position.
        /// </summary>
        /// <param name="position">The position to raise the event with.</param>
        /// <remarks>Only <see cref="AudioManager{T}.Update"/> calls this method.</remarks>
        internal void OnPositionChanged(double position)
        {
            PositionChanged?.Invoke(this, position);
        }

        /// <summary>
        /// Gets or sets the position of this playback, normalized to the range 0 to 1, where 0 is the beginning of the sound and 1 is its end.
        /// </summary>
        /// <remarks>The derived type should ensure that the value is in the range 0 to 1.</remarks>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        public virtual double NormalizedPosition
        {
            get
            {
                ThrowIfDisposed();
                return Position / InternalSound.Length;
            }
            set
            {
                ThrowIfDisposed();
                Position = value * InternalSound.Length;
            }
        }

        /// <summary>
        /// Raised when the normalized position of this playback changes.
        /// </summary>
        public virtual event Action<Playback<T>, double> NormalizedPositionChanged;

        /// <summary>
        /// Raises the <see cref="NormalizedPositionChanged"/> event with the specified normalized position.
        /// </summary>
        /// <param name="normalizedPosition">The normalized position to raise the event with.</param>
        /// <remarks>Only <see cref="AudioManager{T}.Update"/> calls this method.</remarks>
        internal void OnNormalizedPositionChanged(double normalizedPosition)
        {
            NormalizedPositionChanged?.Invoke(this, normalizedPosition);
        }

        /// <summary>
        /// Plays this playback, depending on its <see cref="Status"/>:
        /// <list type="table">
        /// <listheader><term>Status</term><description>Behavior</description></listheader>
        /// <item><term><see cref="PlaybackStatus.None"/></term><description>Plays from the beginning.</description></item>
        /// <item><term><see cref="PlaybackStatus.Paused"/></term><description>Resumes from the current position.</description></item>
        /// </list>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        public abstract void Play();

        /// <summary>
        /// Pauses this playback.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        public abstract void Pause();

        /// <summary>
        /// Stops this playback and resets its status to <see cref="PlaybackStatus.None"/>.
        /// </summary>
        /// <remarks>The derived type should ensure that <see cref="Status"/> becomes <see cref="PlaybackStatus.None"/> after this method is called.</remarks>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        public virtual void Stop()
        {
            Position = 0;
            Pause();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="Playback{T}"/> class.
        /// </summary>
        ~Playback()
        {
            Dispose(false);
        }

        /// <summary>
        /// Releases all resources used by the current instance of the <see cref="Playback{T}"/> class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="Playback{T}"/> class and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    InternalSound             = null;
                    StatusChanged             = null;
                    VolumeChanged             = null;
                    PositionChanged           = null;
                    NormalizedPositionChanged = null;
                }
            }
        }

        /// <summary>
        /// Throws an <see cref="ObjectDisposedException"/> if this playback has been disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This playback has been disposed.</exception>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
