#region Copyright (C) 2007-2014 Team MediaPortal

/*
    Copyright (C) 2007-2014 Team MediaPortal
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

using MediaPortal.Common.MediaManagement;
using MediaPortal.Common.MediaManagement.DefaultItemAspects;
using MediaPortal.UiComponents.Media.General;
using MediaPortal.Utilities;

namespace MediaPortal.UiComponents.Media.Models.Sorting
{
  public class AudioSortByAlbumTrack : SortByTitle
  {
    public override string DisplayName
    {
      get { return Consts.RES_SORT_BY_ALBUM_TRACK; }
    }

    public override int Compare(MediaItem x, MediaItem y)
    {
      MediaItemAspect audioAspectX;
      MediaItemAspect audioAspectY;
      if (x.Aspects.TryGetValue(AudioAspect.ASPECT_ID, out audioAspectX) && y.Aspects.TryGetValue(AudioAspect.ASPECT_ID, out audioAspectY))
      {
        string albumX = (string) audioAspectX.GetAttributeValue(AudioAspect.ATTR_ALBUM);
        string albumY = (string) audioAspectY.GetAttributeValue(AudioAspect.ATTR_ALBUM);
        int res = string.Compare(albumX, albumY);
        if (res != 0)
          return res;
        int? trackX = (int?) audioAspectX.GetAttributeValue(AudioAspect.ATTR_TRACK);
        int? trackY = (int?) audioAspectY.GetAttributeValue(AudioAspect.ATTR_TRACK);
        return ObjectUtils.Compare(trackX, trackY);
      }
      // Fallback if the items to be compared are no audio items: Compare by title
      return base.Compare(x, y);
    }
  }
}
