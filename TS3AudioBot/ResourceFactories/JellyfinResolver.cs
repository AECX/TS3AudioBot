// TS3AudioBot - An advanced Musicbot for Teamspeak 3
// Copyright (C) 2017  TS3AudioBot contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using LiteDB;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using Org.BouncyCastle.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Resources;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TS3AudioBot.Audio;
using TS3AudioBot.Config;
using TS3AudioBot.Helper;
using TS3AudioBot.Localization;
using TS3AudioBot.Playlists;
using TS3AudioBot.ResourceFactories.AudioTags;
using TSLib.Helper;
using static System.Collections.Specialized.BitVector32;

namespace TS3AudioBot.ResourceFactories
{
	public sealed class JellyfinResolver : IResourceResolver, IThumbnailResolver, ISearchResolver, IPlaylistResolver
	{
		private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		private readonly Regex JellyfinResourceRegex;
		private readonly Regex JellyfinPlaylistRegex;

		private string ApiKey => conf.ApiKey;
		private string ParentId => conf.LibraryId;
		private string Hostname => conf.Hostname;

		public string ResolverFor => "jellyfin";

		public MatchCertainty MatchResource(ResolveContext? _, string uri) => this.JellyfinResourceRegex.IsMatch(uri) ? MatchCertainty.Always : MatchCertainty.Never;
		public MatchCertainty MatchPlaylist(ResolveContext ctx, string uri) => this.JellyfinPlaylistRegex.IsMatch(uri)? MatchCertainty.Always : MatchCertainty.Never;

		private ConfResolverJellyfin conf;

		public JellyfinResolver(ConfResolverJellyfin conf)
		{
			this.conf = conf;

			// Regex relies on config so we initialize the matchers here
			this.JellyfinResourceRegex = new Regex(conf.Hostname + @"/Items/([a-f0-9]{32}).*", Util.DefaultRegexConfig);
			this.JellyfinPlaylistRegex = new Regex(conf.Hostname + @"/web/#/details\?id=([a-f0-9]{32}).*", Util.DefaultRegexConfig);
		}

		/// <summary>
		/// Retrieves body from the defined jellyfin api endpoint
		/// </summary>
		/// <param name="path">Full path excluding the hostname</param>
		/// <returns></returns>
		private async Task<string?> GetApi(string path)
		{
			try
			{ 
				string uri = $"{conf.Hostname}/{path}";

				var request = WebRequest.CreateHttp(uri);
				request.Headers.Add($"Authorization: MediaBrowser Client=\"TS3AudioBot\", Token=\"{conf.ApiKey}\"");
				var response = await request.GetResponseAsync();

				using (StreamReader reader = new StreamReader(response.GetResponseStream()))
				{
					return reader.ReadToEnd();
				}
			}
			catch(Exception ex)
			{
				Log.Error(ex);
				return null;
			}
		}

		public async Task<PlayResource> GetResource(ResolveContext? _, string uri)
		{
			AudioResource audioResource;
			var match = JellyfinResourceRegex.Match(uri);

			if (!match.Success)
			{
				// Search instead
				audioResource = (await this.GetAudio(uri)) ?? throw Error.LocalStr(strings.error_media_file_not_found);
			}
			else
			{
				var resourceId = match.Groups[1].Value;
				audioResource = new AudioResource(resourceId, null, this.ResolverFor);
			}

			var play = await this.GetResourceById(_, audioResource);

			return play;
		}

		public async Task<PlayResource> GetResourceById(ResolveContext? _, AudioResource resource)
		{
			var resourceId = resource.ResourceId;
			var uri = $"{conf.Hostname}/Items/{resourceId}/Download?api_key={conf.ApiKey}";
			var play = new PlayResource(uri, resource);
			return play;
		}

		public string RestoreLink(ResolveContext _, AudioResource resource) => $"{conf.Hostname }/Items/{resource.ResourceId}/Download?api_key={conf.ApiKey}";

		public async Task GetThumbnail(ResolveContext _, PlayResource playResource, Func<Stream, Task> action)
		{
			string imageUri = $"https://media.henke.gg/Items/{playResource.AudioResource.ResourceId}/Images/Primary?fillHeight=390&fillWidth=480&quality=96";
			await WebWrapper.Request(imageUri).ToStream(action);
		}

		public async Task<IList<AudioResource>> Search(ResolveContext _, string keyword)
		{
			IList<AudioResource> resources = new List<AudioResource>();

			string apiUrl = $"Items?IncludeItemTypes=Audio&Recursive=true&Limit=10&searchTerm={keyword}";

			var body = await this.GetApi(apiUrl);

			if (body != null)
			{
				var response = JsonConvert.DeserializeObject<JellyfinItemsResponse>(body);

				foreach (var item in response.Items)
				{
					resources.Add(new AudioResource(item.Id, item.fullName, this.ResolverFor));
				}
			}

			return resources;
		}

		private async Task<AudioResource?> GetAudio(string keyword)
		{
			AudioResource resource = null;

			string apiUrl = $"Items?IncludeItemTypes=Audio&Recursive=true&Limit=1&searchTerm={keyword}";

			var body = await this.GetApi(apiUrl);

			if (body != null)
			{
				var response = JsonConvert.DeserializeObject<JellyfinItemsResponse>(body);

				if (response.Items.Count > 0)
				{
					var item = response.Items[0];
					resource = new AudioResource(item.Id, item.fullName, this.ResolverFor);
				}
			}

			return resource;
		}

		public async Task<Playlist> GetPlaylist(ResolveContext ctx, string url)
		{
			Playlist playlist = new Playlist();

			var match = JellyfinPlaylistRegex.Match(url);

			if (match.Success)
			{

				var playlistId = match.Groups[1].Value;
				var apiUri = $"Items?IncludeItemTypes=Audio&Recursive=true&Limit=250&parentId={playlistId}";

				var body = await this.GetApi(apiUri);
				if (body != null)
				{
					var items = JsonConvert.DeserializeObject<JellyfinItemsResponse>(body);

					if (items.Items.Count > 0)
					{
						foreach( var item in items.Items)
						{
							var resource = new AudioResource(item.Id, item.fullName, this.ResolverFor);
							var playlistItem = new PlaylistItem(resource);

							playlist.Add(playlistItem);
						}
					}
				}
			}

			return playlist;
		}

		public void Dispose() { }
			
	}


	internal struct JellyfinItem
	{
		public string fullName => $"{Name} <{AlbumArtist}> [{Album}]";

		[JsonPropertyName("Name")]
		public string Name { get; set; }
		[JsonPropertyName("Album")]
		public string Album { get; set; }
		[JsonPropertyName("Id")]
		public string Id {  get; set; }
		[JsonPropertyName("AlbumArtist")]
		public string AlbumArtist { get; set; }
		[JsonPropertyName("AlbumId")]
		public string AlbumId { get; set; }
		[JsonPropertyName("AlbumPrimaryImageTag")]
		public string AlbumPrimaryImageTag { get; set; }
	}

	internal struct JellyfinItemsResponse
	{
		[JsonPropertyName("Items")]
		public List<JellyfinItem> Items { get; set; }
	}
}
