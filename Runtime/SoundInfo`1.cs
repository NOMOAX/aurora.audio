using System;
using System.Threading.Tasks;

namespace Aurora.Audio
{
    internal sealed class SoundInfo<T> where T : IEquatable<T>
    {
        internal readonly Task<Sound<T>> Task;

        internal int PlaybackCount;

        internal SoundInfo(Task<Sound<T>> task)
        {
            Task = task;
        }
    }
}
