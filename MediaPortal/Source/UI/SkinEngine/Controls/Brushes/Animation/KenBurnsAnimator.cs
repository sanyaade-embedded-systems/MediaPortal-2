#region Copyright (C) 2007-2012 Team MediaPortal

/*
    Copyright (C) 2007-2012 Team MediaPortal
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
using System.Drawing;

namespace MediaPortal.UI.Players.Image.Animation
{
  /// <summary>
  /// The Ken Burns effect uses different pan and zoom operations to animate an image.
  /// </summary>
  public class KenBurnsAnimator : IImageAnimator
  {
    protected static readonly Random _rnd = new Random(DateTime.Now.Millisecond);

    protected AbstractKenBurnsEffect _currentEffect = null;

    public void Initialize()
    {
      _currentEffect = GetRandomKenBurnsEffect();
    }

    public RectangleF GetZoomRect(float animationProgress, Size imageSize, Size outputSize)
    {
      return _currentEffect == null ? RectangleF.Empty : _currentEffect.GetZoomRect(animationProgress, imageSize, outputSize);
    }

    protected AbstractKenBurnsEffect GetRandomKenBurnsEffect()
    {
      return _rnd.Next(2) == 0 ? (AbstractKenBurnsEffect) new KenBurnsZoomEffect() : new KenBurnsPanEffect();
    }
  }
}