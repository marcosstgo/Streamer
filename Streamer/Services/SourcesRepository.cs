using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Streamer.Models;

namespace Streamer.Services
{
    public static class SourcesRepository
    {
        private const string AppFolderName = "CorilloStreamer";
        private const string SourcesFileName = "sources.json";

        public static string GetAppFolder()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetSourcesPath()
        {
            return Path.Combine(GetAppFolder(), SourcesFileName);
        }

        public static async Task EnsureDefaultSourcesAsync()
        {
            var path = GetSourcesPath();
            if (File.Exists(path)) return;

            // Ensure directory exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var defaults = GetDefaultSources();
            var json = JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }

        public static List<Source> GetDefaultSources()
        {
            return new List<Source>
            {
                new Source { Name = "Tears of Steel", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/TearsOfSteel.mp4", Description = "Sci-fi short film (Blender Open Movie)" },
                new Source { Name = "Big Buck Bunny", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4", Description = "Blender animated short" },
                new Source { Name = "Sintel", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/Sintel.mp4", Description = "Blender fantasy animation" },
                new Source { Name = "Elephant's Dream", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ElephantsDream.mp4", Description = "First Blender open movie" },
                new Source { Name = "For Bigger Blazes", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerBlazes.mp4", Description = "Google sample video" },
                new Source { Name = "For Bigger Escape", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerEscapes.mp4", Description = "Google sample video" },
                new Source { Name = "For Bigger Fun", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerFun.mp4", Description = "Google sample video" },
                new Source { Name = "For Bigger Joyrides", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerJoyrides.mp4", Description = "Google sample video" },
                new Source { Name = "Subaru Outback Commercial", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/SubaruOutbackOnStreetAndDirt.mp4", Description = "Car demo video" },
                new Source { Name = "Volkswagen GTI Review", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/VolkswagenGTIReview.mp4", Description = "Car review sample" },
                new Source { Name = "We Are Going On Bullrun", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/WeAreGoingOnBullrun.mp4", Description = "Automotive short" },
                new Source { Name = "What Car Can You Get For A Grand", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/WhatCarCanYouGetForAGrand.mp4", Description = "Car show sample" },
                new Source { Name = "Night of the Living Dead (1968)", Url = "https://archive.org/download/Night_of_the_Living_Dead_AVI/MoviePowderPresentsTheNightOfTheLivingDead_512kb.mp4", Description = "Public domain horror classic" },
                new Source { Name = "The General (1926)", Url = "https://archive.org/download/The_General_Buster_Keaton/The_General_512kb.mp4", Description = "Buster Keaton silent film" },
                new Source { Name = "Nosferatu (1922)", Url = "https://archive.org/download/Nosferatu1922VHS/Nosferatu%20(1922).mp4", Description = "Classic vampire film" },
                new Source { Name = "4K Nature Relaxation Journey 3", Url = "https://archive.org/download/4-k-nature-relaxation-journey-3-wonders-of-the-world-2017-w-healing-ambient-music/4K%20Nature%20Relaxation%20Journey%203-%20WONDERS%20OF%20THE%20WORLD%20%282017%29%20w-%20Healing%20Ambient%20Music.mp4", Description = "Long nature relaxation video" }
            };
        }

        public static async Task<List<Source>> LoadSourcesAsync()
        {
            var path = GetSourcesPath();
            if (!File.Exists(path))
            {
                await EnsureDefaultSourcesAsync().ConfigureAwait(false);
            }

            // Read file and deserialize; let exceptions bubble to caller for UI reporting
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var list = JsonSerializer.Deserialize<List<Source>>(json);
            return list ?? GetDefaultSources();
        }
    }
}
