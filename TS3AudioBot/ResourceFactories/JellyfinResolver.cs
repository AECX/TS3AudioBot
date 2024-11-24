// TS3AudioBot - An advanced Musicbot for Teamspeak 3
// Copyright (C) 2017  TS3AudioBot contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using LiteDB;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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

namespace TS3AudioBot.ResourceFactories
{
	public sealed class JellyfinResolver : IResourceResolver, IPlaylistResolver, IThumbnailResolver, ISearchResolver
	{
		private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		private readonly Regex JellyfinUrlRegex;
		private readonly Regex JellyfinPlaylistRegex;

		private string ApiKey => conf.ApiKey;
		private string ParentId => conf.LibraryId;
		private string Hostname => conf.Hostname;

		public string ResolverFor => "jellyfin";

		public MatchCertainty MatchResource(ResolveContext? _, string uri) => this.JellyfinUrlRegex.IsMatch(uri) ? MatchCertainty.Always : MatchCertainty.Never;
		public MatchCertainty MatchPlaylist(ResolveContext? _, string uri) => this.JellyfinPlaylistRegex.IsMatch(uri) ? MatchCertainty.Always : MatchCertainty.Never;

		private ConfResolverJellyfin conf;

		public JellyfinResolver(ConfResolverJellyfin conf)
		{
			this.conf = conf;

			// Regex relies on config so we initialize the matchers here
			this.JellyfinUrlRegex = new Regex(conf.Hostname + @"/Items/([a-f0-9]{32}).*", Util.DefaultRegexConfig);
			this.JellyfinPlaylistRegex = new Regex(conf.Hostname + @"/web/#/(?:(?:music.html\?topParentId)|(?:details\?id))=([a-f0-9]{32}).*", Util.DefaultRegexConfig);
		}

		/// <summary>
		/// Retrieves body from the defined jellyfin api endpoint
		/// </summary>
		/// <param name="path">Full path excluding the hostname</param>
		/// <returns></returns>
		private async Task<string> GetApi(string path)
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
				return "{Items:[]}";
			}
		}

		public async Task<PlayResource> GetResource(ResolveContext? _, string uri)
		{
			AudioResource audioResource;
			var match = JellyfinUrlRegex.Match(uri);

			if (!match.Success)
			{
				// Search instead
				var searchResult = (await this.Search(_, uri));
				if (searchResult.Count == 0)
					throw Error.LocalStr(strings.error_media_file_not_found);

				audioResource = searchResult.First();
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

		public string RestoreLink(ResolveContext _, AudioResource resource) => $"{conf.Hostname }/Items/{resource.ResourceId}/Download?api_key=${conf.ApiKey}";


		public async Task<Playlist> GetPlaylist(ResolveContext _, string url)
		{
			throw new NotImplementedException();
		}

		public Task GetThumbnail(ResolveContext _, PlayResource playResource, Func<Stream, Task> action)
		{
			// default  :  120px/ 90px /default.jpg
			// medium   :  320px/180px /mqdefault.jpg
			// high     :  480px/360px /hqdefault.jpg
			// standard :  640px/480px /sddefault.jpg
			// maxres   : 1280px/720px /maxresdefault.jpg
			return WebWrapper
				.Request($"https://i.ytimg.com/vi/{playResource.AudioResource.ResourceId}/mqdefault.jpg")
				.ToStream(action);
		}

		public async Task<IList<AudioResource>> Search(ResolveContext _, string keyword)
		{
			IList<AudioResource> resources = new List<AudioResource>();

			var body = await this.GetApi($"Items?IncludeItemTypes=Audio&Recursive=true&Limit=1&searchTerm={keyword}");

			var response = JsonConvert.DeserializeObject<JellyfinItemsResponse>(body);

			if (response.Items.Count > 0)
			{
				var item = response.Items[0];

				resources.Add(new AudioResource(item.Id, item.Name, this.ResolverFor));
			}

			return resources;
		}

		public void Dispose() { }
	}

	internal struct JellyfinItem
	{
		[JsonPropertyName("Name")]
		public string Name { get; set; }
		[JsonPropertyName("Id")]
		public string Id {  get; set; }
		[JsonPropertyName("AlbumArtist")]
		public string AlbumArtist { get; set; }
	}

	internal struct JellyfinItemsResponse
	{
		[JsonPropertyName("Items")]
		public List<JellyfinItem> Items { get; set; }
	}
}
