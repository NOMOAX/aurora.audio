using System;
using System.Threading.Tasks;

namespace Aurora.Audio
{
    internal sealed class SoundInfo<T> where T : notnull, IEquatable<T>
    {
        internal readonly Task<ISound<T>> LoadTask;

        internal int PlaybackCount;

        internal SoundInfo(Task<ISound<T>> loadTask)
        {
            LoadTask = loadTask;
        }
    }
}
