#region Copyright (C) 2007-2010 Team MediaPortal

/*
    Copyright (C) 2007-2010 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using MediaPortal.Core;
using MediaPortal.Core.MediaManagement;
using MediaPortal.Core.MediaManagement.DefaultItemAspects;
using MediaPortal.Core.MediaManagement.ResourceAccess;
using MediaPortal.Utilities;
using TagLib;
using File = TagLib.File;
using MediaPortal.Core.Logging;

namespace MediaPortal.Extensions.MetadataExtractors.MusicMetadataExtractor
{
  /// <summary>
  /// MediaPortal 2 metadata extractor implementation for music files. Supports several formats.
  /// </summary>
  public class MusicMetadataExtractor : IMetadataExtractor
  {
    #region Public constants

    /// <summary>
    /// GUID string for the music metadata extractor.
    /// </summary>
    public const string METADATAEXTRACTOR_ID_STR = "817FEE2E-8690-4355-9F24-3BDC65AEDFFE";

    /// <summary>
    /// Music metadata extractor GUID.
    /// </summary>
    public static Guid METADATAEXTRACTOR_ID = new Guid(METADATAEXTRACTOR_ID_STR);

    #endregion

    #region Protected fields and classes

    protected static IList<string> SHARE_CATEGORIES = new List<string>();
    protected static IList<string> AUDIO_EXTENSIONS = new List<string>();

    /// <summary>
    /// Music file accessor class needed for our tag library implementation. This class maps
    /// the TagLib#'s <see cref="File.IFileAbstraction"/> view to an MP 2 file from a media provider.
    /// </summary>
    protected class MediaProviderFileAbstraction : File.IFileAbstraction
    {
      protected IResourceAccessor _resourceAccessor;

      public MediaProviderFileAbstraction(IResourceAccessor resourceAccessor)
      {
        _resourceAccessor = resourceAccessor;
      }

      #region IFileAbstraction implementation

      public void CloseStream(Stream stream)
      {
        stream.Close();
      }

      public string Name
      {
        get { return _resourceAccessor.ResourcePathName; }
      }

      public Stream ReadStream
      {
        get { return _resourceAccessor.OpenRead(); }
      }

      public Stream WriteStream
      {
        get { return _resourceAccessor.OpenWrite(); }
      }

      #endregion
    }

    protected MetadataExtractorMetadata _metadata;

    #endregion

    #region Ctor

    static MusicMetadataExtractor()
    {
      SHARE_CATEGORIES.Add(DefaultMediaCategory.Audio.ToString());

      AUDIO_EXTENSIONS.Add(".ape");
      AUDIO_EXTENSIONS.Add(".flac");
      AUDIO_EXTENSIONS.Add(".mp3");
      AUDIO_EXTENSIONS.Add(".ogg");
      AUDIO_EXTENSIONS.Add(".wv");
      AUDIO_EXTENSIONS.Add(".wav");
      AUDIO_EXTENSIONS.Add(".wma");
      AUDIO_EXTENSIONS.Add(".mp4");
      AUDIO_EXTENSIONS.Add(".m4a");
      AUDIO_EXTENSIONS.Add(".m4p");
      AUDIO_EXTENSIONS.Add(".mpc");
      AUDIO_EXTENSIONS.Add(".mp+");
      AUDIO_EXTENSIONS.Add(".mpp");
    }

    public MusicMetadataExtractor()
    {
      _metadata = new MetadataExtractorMetadata(METADATAEXTRACTOR_ID, "Music metadata extractor", false,
          SHARE_CATEGORIES, new[]
              {
                MediaAspect.Metadata,
                AudioAspect.Metadata
              });
    }

    #endregion

    #region Protected methods

    /// <summary>
    /// Returns the information if the specified file name (or path) has a file extension which is
    /// supposed to be supported by this metadata extractor.
    /// </summary>
    /// <param name="fileName">Relative or absolute file path to check.</param>
    /// <returns><c>true</c>, if the file's extension is supposed to be supported, else <c>false</c>.</returns>
    protected static bool HasAudioExtension(string fileName)
    {
      string ext = Path.GetExtension(fileName).ToLower();
      return AUDIO_EXTENSIONS.Contains(ext);
    }

    /// <summary>
    /// Given a music file name (or path), this method tries to guess the title of the music. This is done
    /// by looking for a '-' character and taking the succeeding part of the name, or the whole name if
    /// no '-' character is present.
    /// </summary>
    /// <param name="filePath">Absolute or relative music file name.</param>
    /// <returns>Guessed title string.</returns>
    protected static string GuessTitle(string filePath)
    {
      string fileName = Path.GetFileName(filePath);
      int i = fileName.IndexOf('-');
      return i == -1 ? Path.GetFileNameWithoutExtension(fileName) : fileName.Substring(i + 1).Trim();
    }

    /// <summary>
    /// Given a music file name (or path), this method tries to guess the artist(s) of the music. This is done
    /// by looking for a '-' character and taking the preceding part of the name. If no '-' character is
    /// present, an empty enumeration is returned.
    /// </summary>
    /// <param name="filePath">Absolute or relative music file name.</param>
    /// <returns>Guessed artist(s) enumeration.</returns>
    protected static string GuessArtist(string filePath)
    {
      string fileName = Path.GetFileName(filePath);
      // Test form Artist - Title
      int start = fileName.IndexOf('-');
      if (start > -1)
        return fileName.Substring(0, start).Trim();
      // Test form Title (Artist)
      start = fileName.IndexOf('(');
      int end = start == -1 ? -1 : fileName.IndexOf(')', start);
      return end > -1 ? fileName.Substring(start + 1, end - start - 1) : null;
    }

    protected static void PatchID3v2Enumeration(IList<string> valuesList, string part1, string part2)
    {
      int index1 = valuesList.IndexOf(part1);
      int index2 = valuesList.IndexOf(part2);
      if (index1 != -1 && index2 == index1 + 1)
      {
        valuesList.RemoveAt(index2);
        valuesList.RemoveAt(index1);
        valuesList.Insert(index1, part1 + "/" + part2);
      }
    }

    protected static IEnumerable<string> PatchID3v2Enumeration(IEnumerable<string> valuesEnumer)
    {
      // We have to cope with a very stupid problem; The ID3Tag specification v2 (http://www.id3.org/d3v2.3.0, search for TPE1)
      // uses the "/" character to separate artists/performers/lyricists etc., but what to do if an artist name contains
      // that character? We'll do a hack for the most common artists of that kind
      if (valuesEnumer == null)
        return null;
      IList<string> values = new List<string>(valuesEnumer);
      if (values.Count == 0)
        return null;
      PatchID3v2Enumeration(values, "AC", "DC");
      return values;
    }

    #endregion

    #region IMetadataExtractor implementation

    public MetadataExtractorMetadata Metadata
    {
      get { return _metadata; }
    }

    public bool TryExtractMetadata(IResourceAccessor mediaItemAccessor, IDictionary<Guid, MediaItemAspect> extractedAspectData)
    {
      if (!mediaItemAccessor.IsFile)
        return false;
      string humanReadablePath = mediaItemAccessor.ResourcePathName;
      if (!HasAudioExtension(humanReadablePath))
        return false;

      // TODO: The creation of new media item aspects could be moved to a general method
      MediaItemAspect mediaAspect;
      if (!extractedAspectData.TryGetValue(MediaAspect.ASPECT_ID, out mediaAspect))
        extractedAspectData[MediaAspect.ASPECT_ID] = mediaAspect = new MediaItemAspect(MediaAspect.Metadata);
      MediaItemAspect musicAspect;
      if (!extractedAspectData.TryGetValue(AudioAspect.ASPECT_ID, out musicAspect))
        extractedAspectData[AudioAspect.ASPECT_ID] = musicAspect = new MediaItemAspect(AudioAspect.Metadata);

      try
      {
        File tag;
        try
        {
          ByteVector.UseBrokenLatin1Behavior = true;  // Otherwise we have problems retrieving non-latin1 chars
          tag = File.Create(new MediaProviderFileAbstraction(mediaItemAccessor));

        }
        catch (CorruptFileException)
        {
          // Only log at the info level here - And simply return false. This makes the importer know that we
          // couldn't perform our task here
          ServiceRegistration.Get<ILogger>().Info("MusicMetadataExtractor: Music file '{0}' seems to be broken", mediaItemAccessor.LocalResourcePath);
          return false;
        }

        string title = string.IsNullOrEmpty(tag.Tag.Title) ? GuessTitle(humanReadablePath) : tag.Tag.Title;
        IEnumerable<string> artists;
        if (tag.Tag.Performers.Length > 0)
          artists = tag.Tag.Performers;
        else
        {
          string artist = GuessArtist(humanReadablePath);
          artists = artist == null ? null : new string[] {artist};
        }
        mediaAspect.SetAttribute(MediaAspect.ATTR_TITLE, title);
        // FIXME Albert: tag.MimeType returns taglib/mp3 for an MP3 file. This is not what we want and collides with the
        // mimetype handling in the BASS player, which expects audio/xxx.
        //mediaAspect.SetAttribute(MediaAspect.ATTR_MIME_TYPE, tag.MimeType);
        musicAspect.SetCollectionAttribute(AudioAspect.ATTR_ARTISTS, PatchID3v2Enumeration(artists));
        musicAspect.SetAttribute(AudioAspect.ATTR_ALBUM, StringUtils.TrimToNull(tag.Tag.Album));
        musicAspect.SetCollectionAttribute(AudioAspect.ATTR_ALBUMARTISTS, PatchID3v2Enumeration(tag.Tag.AlbumArtists));
        musicAspect.SetAttribute(AudioAspect.ATTR_BITRATE, tag.Properties.AudioBitrate);
        mediaAspect.SetAttribute(MediaAspect.ATTR_COMMENT, StringUtils.TrimToNull(tag.Tag.Comment));
        musicAspect.SetCollectionAttribute(AudioAspect.ATTR_COMPOSERS, tag.Tag.Composers);
        // The following code gets cover art images - and there is no cover art attribute in any media item aspect
        // defined yet. (Albert, 2008-11-19)
        //IPicture[] pics = new IPicture[] { };
        //pics = tag.Tag.Pictures;
        //if (pics.Length > 0)
        //{
        //  musictag.CoverArtImageBytes = pics[0].Data.Data;
        //}
        musicAspect.SetAttribute(AudioAspect.ATTR_DURATION, (long) tag.Properties.Duration.TotalSeconds);
        if (tag.Tag.Genres.Length > 0)
          musicAspect.SetCollectionAttribute(AudioAspect.ATTR_GENRES, tag.Tag.Genres);
        if (tag.Tag.Track != 0)
          musicAspect.SetAttribute(AudioAspect.ATTR_TRACK, (int) tag.Tag.Track);
        if (tag.Tag.TrackCount != 0)
          musicAspect.SetAttribute(AudioAspect.ATTR_NUMTRACKS, (int) tag.Tag.TrackCount);
        int year = (int) tag.Tag.Year;
        if (year >= 30 && year <= 99)
          year += 1900;
        if (year >= 1930 && year <= 2030)
          mediaAspect.SetAttribute(MediaAspect.ATTR_RECORDINGTIME, new DateTime(year, 1, 1));
        return true;
      }
      catch (UnsupportedFormatException)
      {
        ServiceRegistration.Get<ILogger>().Info("MusicMetadataExtractor: Unsupported music file '{0}'", mediaItemAccessor.LocalResourcePath);
        return false;
      }
      catch (Exception e)
      {
        // Only log at the info level here - And simply return false. This makes the importer know that we
        // couldn't perform our task here
        ServiceRegistration.Get<ILogger>().Info("MusicMetadataExtractor: Exception reading resource '{0}' (Text: '{1}')", mediaItemAccessor.LocalResourcePath, e.Message);
      }
      return false;
    }

    #endregion
  }
}
