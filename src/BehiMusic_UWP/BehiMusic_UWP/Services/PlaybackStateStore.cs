using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace BehiMusic_UWP.Services
{
    [DataContract]
    internal sealed class SavedPlaybackState
    {
        [DataMember(Order = 1)] public List<string> QueuePaths { get; set; }
        [DataMember(Order = 2)] public string CurrentPath { get; set; }
        [DataMember(Order = 3)] public long PositionTicks { get; set; }
        [DataMember(Order = 4)] public bool ShuffleEnabled { get; set; }
        [DataMember(Order = 5)] public int RepeatMode { get; set; }
    }

    internal static class PlaybackStateStore
    {
        private const string StateFileName = "playback-state.json";

        public static async Task SaveAsync(SavedPlaybackState state)
        {
            StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(StateFileName, CreationCollisionOption.ReplaceExisting);
            using (Stream stream = await file.OpenStreamForWriteAsync())
            {
                stream.SetLength(0);
                new DataContractJsonSerializer(typeof(SavedPlaybackState)).WriteObject(stream, state);
                await stream.FlushAsync();
            }
        }

        public static async Task<SavedPlaybackState> LoadAsync()
        {
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(StateFileName);
                using (Stream stream = await file.OpenStreamForReadAsync())
                    return new DataContractJsonSerializer(typeof(SavedPlaybackState)).ReadObject(stream) as SavedPlaybackState;
            }
            catch
            {
                return null;
            }
        }
    }
}
