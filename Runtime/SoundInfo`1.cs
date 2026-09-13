using System;
using System.Threading.Tasks;

namespace Aurora.Audio
{
    internal sealed class SoundInfo<T> where T : IEquatable<T>
    {
        internal readonly Task<Sound<T>> LoadTask;

        internal int PlaybackCount;

        internal SoundInfo(Task<Sound<T>> loadTask)
        {
            LoadTask = loadTask;
        }
    }
}
