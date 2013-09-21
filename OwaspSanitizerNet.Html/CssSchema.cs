// Copyright (c) 2013, Mike Samuel
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions
// are met:
//
// Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
// Redistributions in binary form must reproduce the above copyright
// notice, this list of conditions and the following disclaimer in the
// documentation and/or other materials provided with the distribution.
// Neither the name of the OWASP nor the names of its contributors may
// be used to endorse or promote products derived from this software
// without specific prior written permission.
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
// FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
// COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
// BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
// LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
// ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Linq;

namespace OwaspSanitizerNet.Html
{
/** Describes the kinds of tokens a CSS property's value can safely contain. */
[TCB]
public sealed class CssSchema {

  internal class Property {
    /** A bitfield of BIT_* constants describing groups of allowed tokens. */
    internal readonly int bits;
    /** Specific allowed values. */
    internal readonly HashSet<String> literals;
    /**
     * Maps lower-case function tokens to the schema key for their parameters.
     */
    internal readonly Dictionary<String, String> fnKeys;

    internal Property(
        int bits, HashSet<String> literals,
        Dictionary<String, String> fnKeys) {
      this.bits = bits;
      this.literals = literals;
      this.fnKeys = fnKeys;
    }
  }

  internal const int BIT_QUANTITY = 1;
  internal const int BIT_HASH_VALUE = 2;
  internal const int BIT_NEGATIVE = 4;
  internal const int BIT_STRING = 8;
  internal const int BIT_URL = 16;
  internal const int BIT_UNRESERVED_WORD = 64;
  internal const int BIT_UNICODE_RANGE = 128;

  static readonly Property DISALLOWED = new Property(
      0, new HashSet<string>(), new Dictionary<String, String>());

  private readonly Dictionary<String, Property> properties;

  private CssSchema(Dictionary<String, Property> properties) {
    if (properties == null) { throw new ArgumentNullException(); }
    this.properties = properties;
  }

  /**
   * A schema that includes all and only the named properties.
   *
   * @param propertyNames a series of lower-case CSS property names that appear
   *    in the built-in CSS definitions.  It is an error to mention an unknown
   *    property name.  This class's {@code main} method will dump a list of
   *    known property names when run with zero arguments.
   */
  public static CssSchema withProperties(
      IEnumerable<String> propertyNames) {
    var properties = new Dictionary<string, Property>();
    foreach (String propertyName in propertyNames) {
      Property prop;
      if (!DEFINITIONS.TryGetValue(propertyName, out prop)) { throw new ArgumentException(propertyName); }
      properties.Add(propertyName, prop);
    }
    return new CssSchema(properties);
  }

  /**
   * A schema that represents the union of the input schemas.
   *
   * @return A schema that allows all and only CSS properties that are allowed
   *    by at least one of the inputs.
   */
  public static CssSchema Union(params CssSchema[] cssSchemas) {
    if (cssSchemas.Length == 1) { return cssSchemas[0]; }
    var properties = cssSchemas.SelectMany(cssSchema => cssSchema.properties)
        .ToDictionary(prop => prop.Key, prop => prop.Value);
      return new CssSchema(properties);
  }

  /**
   * The set of CSS properties allowed by this schema.
   *
   * @return an immutable set.
   */
  public HashSet<String> allowedProperties() {
    return new HashSet<string>(properties.Keys);
  }

  /** The schema for the named property or function key. */
  internal Property forKey(String propertyName) {
    propertyName = propertyName.ToLowerInvariant();
    Property property;
    if (properties.TryGetValue(propertyName, out property)) { return property; }
    int n = propertyName.Length;
    if (n != 0 && propertyName[0] == '-') {
      String barePropertyName = StripVendorPrefix(propertyName);
      if (properties.TryGetValue(barePropertyName, out property)) { return property; }
    }
    return DISALLOWED;
  }

  /** {@code "-moz-foo"} &rarr; {@code "foo"}. */
  private static String StripVendorPrefix(String cssKeyword) {
    int prefixLen = 0;
    switch (cssKeyword[1]) {
      case 'm':
        if (cssKeyword.StartsWith("-ms-")) {
          prefixLen = 4;
        } else if (cssKeyword.StartsWith("-moz-")) {
          prefixLen = 5;
        }
        break;
      case 'o':
        if (cssKeyword.StartsWith("-o-")) { prefixLen = 3; }
        break;
      case 'w':
        if (cssKeyword.StartsWith("-webkit-")) { prefixLen = 8; }
        break;
    }
    return prefixLen == 0 ? null : cssKeyword.Substring(prefixLen);
  }

  /** Maps lower-cased CSS property names to information about them. */
  internal static readonly Dictionary<String, Property> DEFINITIONS;
  static CssSchema() {
    var zeroFns = new Dictionary<string, string>();
    var definitions = new Dictionary<string, Property>();
    var mozBorderRadiusLiterals0 = new HashSet<String>(new [] { "/" });
    var mozOpacityLiterals0 = new HashSet<String>(new [] { "inherit" });
    var mozOutlineLiterals0 = new HashSet<String>(new [] { 
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige",
        "bisque", "black", "blanchedalmond", "blue", "blueviolet", "brown",
        "burlywood", "cadetblue", "chartreuse", "chocolate", "coral",
        "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan",
        "darkgoldenrod", "darkgray", "darkgreen", "darkkhaki", "darkmagenta",
        "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon",
        "darkseagreen", "darkslateblue", "darkslategray", "darkturquoise",
        "darkviolet", "deeppink", "deepskyblue", "dimgray", "dodgerblue",
        "firebrick", "floralwhite", "forestgreen", "fuchsia", "gainsboro",
        "ghostwhite", "gold", "goldenrod", "gray", "green", "greenyellow",
        "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki",
        "lavender", "lavenderblush", "lawngreen", "lemonchiffon", "lightblue",
        "lightcoral", "lightcyan", "lightgoldenrodyellow", "lightgreen",
        "lightgrey", "lightpink", "lightsalmon", "lightseagreen",
        "lightskyblue", "lightslategray", "lightsteelblue", "lightyellow",
        "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine",
        "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen",
        "mediumslateblue", "mediumspringgreen", "mediumturquoise",
        "mediumvioletred", "midnightblue", "mintcream", "mistyrose",
        "moccasin", "navajowhite", "navy", "oldlace", "olive", "olivedrab",
        "orange", "orangered", "orchid", "palegoldenrod", "palegreen",
        "paleturquoise", "palevioletred", "papayawhip", "peachpuff", "peru",
        "pink", "plum", "powderblue", "purple", "red", "rosybrown", "royalblue",
        "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell", "sienna",
        "silver", "skyblue", "slateblue", "slategray", "snow", "springgreen",
        "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet",
        "wheat", "white", "whitesmoke", "yellow", "yellowgreen" });
    var mozOutlineLiterals1 = new HashSet<String>(new [] { 
        "dashed", "dotted", "double", "groove", "outset", "ridge", "solid" });
    var mozOutlineLiterals2 = new HashSet<String>(new [] { "thick", "thin" });
    var mozOutlineLiterals3 = new HashSet<String>(new [] { 
        "hidden", "inherit", "inset", "invert", "medium", "none" });
    var mozOutlineFunctions =
      new Dictionary<string, string> {{"rgb(", "rgb()" }, {"rgba(", "rgba()"}};
    var mozOutlineColorLiterals0 =
      new HashSet<String>(new [] { "inherit", "invert" });
    var mozOutlineStyleLiterals0 =
      new HashSet<String>(new [] { "hidden", "inherit", "inset", "none" });
    var mozOutlineWidthLiterals0 =
      new HashSet<String>(new [] { "inherit", "medium" });
    var oTextOverflowLiterals0 =
      new HashSet<String>(new [] { "clip", "ellipsis" });
    var azimuthLiterals0 = new HashSet<String>(new [] { 
        "behind", "center-left", "center-right", "far-left", "far-right",
        "left-side", "leftwards", "right-side", "rightwards" });
    var azimuthLiterals1 = new HashSet<String>(new [] { "left", "right" });
    var azimuthLiterals2 =
      new HashSet<String>(new [] { "center", "inherit" });
    var backgroundLiterals0 = new HashSet<String>(new [] { 
        "border-box", "contain", "content-box", "cover", "padding-box" });
    var backgroundLiterals1 =
      new HashSet<String>(new [] { "no-repeat", "repeat-x", "repeat-y", "round", "space" });
    var backgroundLiterals2 = new HashSet<String>(new [] { "bottom", "top" });
    var backgroundLiterals3 = new HashSet<String>(new [] { 
        ",", "/", "auto", "center", "fixed", "inherit", "local", "none",
        "repeat", "scroll", "transparent" });
    var backgroundFunctions =
      new Dictionary<string, string> {
      {"image(", "image()"},
      {"linear-gradient(", "linear-gradient()"},
      {"radial-gradient(", "radial-gradient()"},
      {"repeating-linear-gradient(", "repeating-linear-gradient()"},
      {"repeating-radial-gradient(", "repeating-radial-gradient()"},
      {"rgb(", "rgb()" },
      {"rgba(", "rgba()"},
      };
    var backgroundAttachmentLiterals0 =
      new HashSet<String>(new [] { ",", "fixed", "local", "scroll" });
    var backgroundColorLiterals0 =
      new HashSet<String>(new [] { "inherit", "transparent" });
    var backgroundImageLiterals0 =
      new HashSet<String>(new [] { ",", "none" });
    var backgroundImageFunctions =
      new Dictionary<String, String>
      {
          { "image(", "image()" },
          { "linear-gradient(","linear-gradient()" },
          { "radial-gradient(","radial-gradient()" },
          { "repeating-linear-gradient(","repeating-linear-gradient()" },
          { "repeating-radial-gradient(","repeating-radial-gradient()" }
      };
    var backgroundPositionLiterals0 = new HashSet<String>(new [] { 
        ",", "center" });
    var backgroundRepeatLiterals0 = new HashSet<String>(new [] { 
        ",", "repeat" });
    var borderLiterals0 = new HashSet<String>(new [] { 
        "hidden", "inherit", "inset", "medium", "none", "transparent" });
    var borderCollapseLiterals0 = new HashSet<String>(new [] { 
        "collapse", "inherit", "separate" });
    var bottomLiterals0 = new HashSet<String>(new [] { "auto", "inherit" });
    var boxShadowLiterals0 = new HashSet<String>(new [] { 
        ",", "inset", "none" });
    var clearLiterals0 = new HashSet<String>(new [] { 
        "both", "inherit", "none" });
    var clipFunctions =
        new Dictionary<String, String> {{"rect(", "rect()"}};
    var contentLiterals0 = new HashSet<String>(new [] { "none", "normal" });
    var cueLiterals0 = new HashSet<String>(new [] { "inherit", "none" });
    var cursorLiterals0 = new HashSet<String>(new [] { 
        "all-scroll", "col-resize", "crosshair", "default", "e-resize",
        "hand", "help", "move", "n-resize", "ne-resize", "no-drop",
        "not-allowed", "nw-resize", "pointer", "progress", "row-resize",
        "s-resize", "se-resize", "sw-resize", "text", "vertical-text",
        "w-resize", "wait" });
    var cursorLiterals1 = new HashSet<String>(new [] { 
        ",", "auto", "inherit" });
    var directionLiterals0 = new HashSet<String>(new [] { "ltr", "rtl" });
    var displayLiterals0 = new HashSet<String>(new [] { 
        "-moz-inline-box", "-moz-inline-stack", "block", "inline",
        "inline-block", "inline-table", "list-item", "run-in", "table",
        "table-caption", "table-cell", "table-column", "table-column-group",
        "table-footer-group", "table-header-group", "table-row",
        "table-row-group" });
    var elevationLiterals0 = new HashSet<String>(new [] { 
        "above", "below", "higher", "level", "lower" });
    var emptyCellsLiterals0 = new HashSet<String>(new [] { "hide", "show" });
    //Dictionary<String, String> filterFunctions =
    //  Dictionary.<String, String>of("alpha(", "alpha()");
    var fontLiterals0 = new HashSet<String>(new [] { 
        "100", "200", "300", "400", "500", "600", "700", "800", "900", "bold",
        "bolder", "lighter" });
    var fontLiterals1 = new HashSet<String>(new [] { 
        "large", "larger", "small", "smaller", "x-large", "x-small",
        "xx-large", "xx-small" });
    var fontLiterals2 = new HashSet<String>(new [] { 
        "caption", "icon", "menu", "message-box", "small-caption",
        "status-bar" });
    var fontLiterals3 = new HashSet<String>(new [] { 
        "cursive", "fantasy", "monospace", "sans-serif", "serif" });
    var fontLiterals4 = new HashSet<String>(new [] { "italic", "oblique" });
    var fontLiterals5 = new HashSet<String>(new [] { 
        ",", "/", "inherit", "medium", "normal", "small-caps" });
    var fontFamilyLiterals0 = new HashSet<String>(new [] { ",", "inherit" });
    var fontStretchLiterals0 = new HashSet<String>(new [] { 
        "condensed", "expanded", "extra-condensed", "extra-expanded",
        "narrower", "semi-condensed", "semi-expanded", "ultra-condensed",
        "ultra-expanded", "wider" });
    var fontStretchLiterals1 = new HashSet<String>(new [] { "normal" });
    var fontStyleLiterals0 = new HashSet<String>(new [] { 
        "inherit", "normal" });
    var fontVariantLiterals0 = new HashSet<String>(new [] { 
        "inherit", "normal", "small-caps" });
    var listStyleLiterals0 = new HashSet<String>(new [] { 
        "armenian", "cjk-decimal", "decimal", "decimal-leading-zero", "disc",
        "disclosure-closed", "disclosure-open", "ethiopic-numeric", "georgian",
        "hebrew", "hiragana", "hiragana-iroha", "japanese-formal",
        "japanese-informal", "katakana", "katakana-iroha",
        "korean-hangul-formal", "korean-hanja-formal",
        "korean-hanja-informal", "lower-alpha", "lower-greek", "lower-latin",
        "lower-roman", "simp-chinese-formal", "simp-chinese-informal",
        "square", "trad-chinese-formal", "trad-chinese-informal",
        "upper-alpha", "upper-latin", "upper-roman" });
    var listStyleLiterals1 = new HashSet<String>(new [] { 
        "inside", "outside" });
    var listStyleLiterals2 = new HashSet<String>(new [] { 
        "circle", "inherit", "none" });
    var maxHeightLiterals0 = new HashSet<String>(new [] { 
        "auto", "inherit", "none" });
    var overflowLiterals0 = new HashSet<String>(new [] { 
        "auto", "hidden", "inherit", "scroll", "visible" });
    var overflowXLiterals0 = new HashSet<String>(new [] { 
        "no-content", "no-display" });
    var overflowXLiterals1 = new HashSet<String>(new [] { 
        "auto", "hidden", "scroll", "visible" });
    var pageBreakAfterLiterals0 = new HashSet<String>(new [] { 
        "always", "auto", "avoid", "inherit" });
    var pageBreakInsideLiterals0 = new HashSet<String>(new [] { 
        "auto", "avoid", "inherit" });
    var pitchLiterals0 = new HashSet<String>(new [] { 
        "high", "low", "x-high", "x-low" });
    var playDuringLiterals0 = new HashSet<String>(new [] { 
        "auto", "inherit", "mix", "none", "repeat" });
    var positionLiterals0 = new HashSet<String>(new [] { 
        "absolute", "relative", "static" });
    var speakLiterals0 = new HashSet<String>(new [] { 
        "inherit", "none", "normal", "spell-out" });
    var speakHeaderLiterals0 = new HashSet<String>(new [] { 
        "always", "inherit", "once" });
    var speakNumeralLiterals0 = new HashSet<String>(new [] { 
        "continuous", "digits" });
    var speakPunctuationLiterals0 = new HashSet<String>(new [] { 
        "code", "inherit", "none" });
    var speechRateLiterals0 = new HashSet<String>(new [] { 
        "fast", "faster", "slow", "slower", "x-fast", "x-slow" });
    var tableLayoutLiterals0 = new HashSet<String>(new [] { 
        "auto", "fixed", "inherit" });
    var textAlignLiterals0 = new HashSet<String>(new [] { 
        "center", "inherit", "justify" });
    var textDecorationLiterals0 = new HashSet<String>(new [] { 
        "blink", "line-through", "overline", "underline" });
    var textTransformLiterals0 = new HashSet<String>(new [] { 
        "capitalize", "lowercase", "uppercase" });
    var textWrapLiterals0 = new HashSet<String>(new [] { 
        "suppress", "unrestricted" });
    var unicodeBidiLiterals0 = new HashSet<String>(new [] { 
        "bidi-override", "embed" });
    var verticalAlignLiterals0 = new HashSet<String>(new [] { 
        "baseline", "middle", "sub", "super", "text-bottom", "text-top" });
    var visibilityLiterals0 = new HashSet<String>(new [] { 
        "collapse", "hidden", "inherit", "visible" });
    var voiceFamilyLiterals0 = new HashSet<String>(new [] { 
        "child", "female", "male" });
    var volumeLiterals0 = new HashSet<String>(new [] { 
        "loud", "silent", "soft", "x-loud", "x-soft" });
    var whiteSpaceLiterals0 = new HashSet<String>(new [] { 
        "-moz-pre-wrap", "-o-pre-wrap", "-pre-wrap", "nowrap", "pre",
        "pre-line", "pre-wrap" });
    var wordWrapLiterals0 = new HashSet<String>(new [] { 
        "break-word", "normal" });
    var rgbFunLiterals0 = new HashSet<String>(new [] { "," });
    var linearGradientFunLiterals0 = new HashSet<String>(new [] { 
        ",", "to" });
    var radialGradientFunLiterals0 = new HashSet<String>(new [] { 
        "at", "closest-corner", "closest-side", "ellipse", "farthest-corner",
        "farthest-side" });
    var radialGradientFunLiterals1 = new HashSet<String>(new [] { 
        ",", "center", "circle" });
    var rectFunLiterals0 = new HashSet<String>(new [] { ",", "auto" });
    //var alpha$FunLiterals0 = new HashSet<String>(new [] { "=", "opacity" });
    var mozBorderRadius =
       new Property(5, mozBorderRadiusLiterals0, zeroFns);
    definitions.Add("-moz-border-radius", mozBorderRadius);
    var mozBorderRadiusBottomleft =
       new Property(5, new HashSet<string>(), zeroFns);
    definitions.Add("-moz-border-radius-bottomleft", mozBorderRadiusBottomleft);
    var mozOpacity = new Property(1, mozOpacityLiterals0, zeroFns);
    definitions.Add("-moz-opacity", mozOpacity);
    var mozOutline = new Property(
        7,
        Union(mozOutlineLiterals0, mozOutlineLiterals1, mozOutlineLiterals2,
              mozOutlineLiterals3),
        mozOutlineFunctions);
    definitions.Add("-moz-outline", mozOutline);
    var mozOutlineColor = new Property(
        2, Union(mozOutlineColorLiterals0, mozOutlineLiterals0),
        mozOutlineFunctions);
    definitions.Add("-moz-outline-color", mozOutlineColor);
    var mozOutlineStyle = new Property(
        0, Union(mozOutlineLiterals1, mozOutlineStyleLiterals0), zeroFns);
    definitions.Add("-moz-outline-style", mozOutlineStyle);
    var mozOutlineWidth = new Property(
        5, Union(mozOutlineLiterals2, mozOutlineWidthLiterals0), zeroFns);
    definitions.Add("-moz-outline-width", mozOutlineWidth);
    var oTextOverflow = new Property(0, oTextOverflowLiterals0, zeroFns);
    definitions.Add("-o-text-overflow", oTextOverflow);
    var azimuth = new Property(
        5, Union(azimuthLiterals0, azimuthLiterals1, azimuthLiterals2),
        zeroFns);
    definitions.Add("azimuth", azimuth);
    var background = new Property(
        23,
        Union(azimuthLiterals1, backgroundLiterals0, backgroundLiterals1,
              backgroundLiterals2, backgroundLiterals3, mozOutlineLiterals0),
        backgroundFunctions);
    definitions.Add("background", background);
    definitions.Add("background-attachment",
                new Property(0, backgroundAttachmentLiterals0, zeroFns));
    var backgroundColor = new Property(
        258, Union(backgroundColorLiterals0, mozOutlineLiterals0),
        mozOutlineFunctions);
    definitions.Add("background-color", backgroundColor);
    definitions.Add("background-image",
                new Property(16, backgroundImageLiterals0,
                             backgroundImageFunctions));
    var backgroundPosition = new Property(
        5,
        Union(azimuthLiterals1, backgroundLiterals2,
              backgroundPositionLiterals0),
        zeroFns);
    definitions.Add("background-position", backgroundPosition);
    var backgroundRepeat = new Property(
        0, Union(backgroundLiterals1, backgroundRepeatLiterals0), zeroFns);
    definitions.Add("background-repeat", backgroundRepeat);
    var border = new Property(
        7,
        Union(borderLiterals0, mozOutlineLiterals0, mozOutlineLiterals1,
              mozOutlineLiterals2),
        mozOutlineFunctions);
    definitions.Add("border", border);
    var borderBottomColor = new Property(
        2, Union(backgroundColorLiterals0, mozOutlineLiterals0),
        mozOutlineFunctions);
    definitions.Add("border-bottom-color", borderBottomColor);
    definitions.Add("border-collapse",
                new Property(0, borderCollapseLiterals0, zeroFns));
    var borderSpacing = new Property(5, mozOpacityLiterals0, zeroFns);
    definitions.Add("border-spacing", borderSpacing);
    var bottom = new Property(5, bottomLiterals0, zeroFns);
    definitions.Add("bottom", bottom);
    var boxShadow = new Property(
        7, Union(boxShadowLiterals0, mozOutlineLiterals0), mozOutlineFunctions);
    definitions.Add("box-shadow", boxShadow);
    var captionSide = new Property(
        0, Union(backgroundLiterals2, mozOpacityLiterals0), zeroFns);
    definitions.Add("caption-side", captionSide);
    var clear = new Property(
        0, Union(azimuthLiterals1, clearLiterals0), zeroFns);
    definitions.Add("clear", clear);
    definitions.Add("clip", new Property(0, bottomLiterals0, clipFunctions));
    var color = new Property(
        258, Union(mozOpacityLiterals0, mozOutlineLiterals0),
        mozOutlineFunctions);
    definitions.Add("color", color);
    definitions.Add("content", new Property(8, contentLiterals0, zeroFns));
    var cue = new Property(16, cueLiterals0, zeroFns);
    definitions.Add("cue", cue);
    var cursor = new Property(
        272, Union(cursorLiterals0, cursorLiterals1), zeroFns);
    definitions.Add("cursor", cursor);
    var direction = new Property(
        0, Union(directionLiterals0, mozOpacityLiterals0), zeroFns);
    definitions.Add("direction", direction);
    var display = new Property(
        0, Union(cueLiterals0, displayLiterals0), zeroFns);
    definitions.Add("display", display);
    var elevation = new Property(
        5, Union(elevationLiterals0, mozOpacityLiterals0), zeroFns);
    definitions.Add("elevation", elevation);
    var emptyCells = new Property(
        0, Union(emptyCellsLiterals0, mozOpacityLiterals0), zeroFns);
    definitions.Add("empty-cells", emptyCells);
    //definitions.Add("filter",
    //            new Property(0, HashSet.<String>of(), filterFunctions));
    var cssFloat = new Property(
        0, Union(azimuthLiterals1, cueLiterals0), zeroFns);
    definitions.Add("float", cssFloat);
    var font = new Property(
        73,
        Union(fontLiterals0, fontLiterals1, fontLiterals2, fontLiterals3,
              fontLiterals4, fontLiterals5),
        zeroFns);
    definitions.Add("font", font);
    var fontFamily = new Property(
        72, Union(fontFamilyLiterals0, fontLiterals3), zeroFns);
    definitions.Add("font-family", fontFamily);
    var fontSize = new Property(
        1, Union(fontLiterals1, mozOutlineWidthLiterals0), zeroFns);
    definitions.Add("font-size", fontSize);
    var fontStretch = new Property(
        0, Union(fontStretchLiterals0, fontStretchLiterals1), zeroFns);
    definitions.Add("font-stretch", fontStretch);
    var fontStyle = new Property(
        0, Union(fontLiterals4, fontStyleLiterals0), zeroFns);
    definitions.Add("font-style", fontStyle);
    definitions.Add("font-variant", new Property(
        0, fontVariantLiterals0, zeroFns));
    var fontWeight = new Property(
        0, Union(fontLiterals0, fontStyleLiterals0), zeroFns);
    definitions.Add("font-weight", fontWeight);
    var height = new Property(5, bottomLiterals0, zeroFns);
    definitions.Add("height", height);
    var letterSpacing = new Property(5, fontStyleLiterals0, zeroFns);
    definitions.Add("letter-spacing", letterSpacing);
    definitions.Add("line-height", new Property(1, fontStyleLiterals0, zeroFns));
    var listStyle = new Property(
        16,
        Union(listStyleLiterals0, listStyleLiterals1, listStyleLiterals2),
        backgroundImageFunctions);
    definitions.Add("list-style", listStyle);
    definitions.Add("list-style-image", new Property(
        16, cueLiterals0, backgroundImageFunctions));
    var listStylePosition = new Property(
        0, Union(listStyleLiterals1, mozOpacityLiterals0), zeroFns);
    definitions.Add("list-style-position", listStylePosition);
    var listStyleType = new Property(
        0, Union(listStyleLiterals0, listStyleLiterals2), zeroFns);
    definitions.Add("list-style-type", listStyleType);
    var margin = new Property(1, bottomLiterals0, zeroFns);
    definitions.Add("margin", margin);
    var maxHeight = new Property(1, maxHeightLiterals0, zeroFns);
    definitions.Add("max-height", maxHeight);
    var opacity = new Property(1, mozOpacityLiterals0, zeroFns);
    definitions.Add("opacity", opacity);
    definitions.Add("overflow", new Property(0, overflowLiterals0, zeroFns));
    var overflowX = new Property(
        0, Union(overflowXLiterals0, overflowXLiterals1), zeroFns);
    definitions.Add("overflow-x", overflowX);
    var padding = new Property(1, mozOpacityLiterals0, zeroFns);
    definitions.Add("padding", padding);
    var pageBreakAfter = new Property(
        0, Union(azimuthLiterals1, pageBreakAfterLiterals0), zeroFns);
    definitions.Add("page-break-after", pageBreakAfter);
    definitions.Add("page-break-inside", new Property(
        0, pageBreakInsideLiterals0, zeroFns));
    var pitch = new Property(
        5, Union(mozOutlineWidthLiterals0, pitchLiterals0), zeroFns);
    definitions.Add("pitch", pitch);
    definitions.Add("play-during", new Property(
        16, playDuringLiterals0, zeroFns));
    var position = new Property(
        0, Union(mozOpacityLiterals0, positionLiterals0), zeroFns);
    definitions.Add("position", position);
    definitions.Add("quotes", new Property(8, cueLiterals0, zeroFns));
    definitions.Add("speak", new Property(0, speakLiterals0, zeroFns));
    definitions.Add("speak-header", new Property(
        0, speakHeaderLiterals0, zeroFns));
    var speakNumeral = new Property(
        0, Union(mozOpacityLiterals0, speakNumeralLiterals0), zeroFns);
    definitions.Add("speak-numeral", speakNumeral);
    definitions.Add("speak-punctuation", new Property(
        0, speakPunctuationLiterals0, zeroFns));
    var speechRate = new Property(
        5, Union(mozOutlineWidthLiterals0, speechRateLiterals0), zeroFns);
    definitions.Add("speech-rate", speechRate);
    definitions.Add("table-layout", new Property(
        0, tableLayoutLiterals0, zeroFns));
    var textAlign = new Property(
        0, Union(azimuthLiterals1, textAlignLiterals0), zeroFns);
    definitions.Add("text-align", textAlign);
    var textDecoration = new Property(
        0, Union(cueLiterals0, textDecorationLiterals0), zeroFns);
    definitions.Add("text-decoration", textDecoration);
    var textTransform = new Property(
        0, Union(cueLiterals0, textTransformLiterals0), zeroFns);
    definitions.Add("text-transform", textTransform);
    var textWrap = new Property(
        0, Union(contentLiterals0, textWrapLiterals0), zeroFns);
    definitions.Add("text-wrap", textWrap);
    var unicodeBidi = new Property(
        0, Union(fontStyleLiterals0, unicodeBidiLiterals0), zeroFns);
    definitions.Add("unicode-bidi", unicodeBidi);
    var verticalAlign = new Property(
        5,
        Union(backgroundLiterals2, mozOpacityLiterals0, verticalAlignLiterals0),
        zeroFns);
    definitions.Add("vertical-align", verticalAlign);
    definitions.Add("visibility", new Property(0, visibilityLiterals0, zeroFns));
    var voiceFamily = new Property(
        8, Union(fontFamilyLiterals0, voiceFamilyLiterals0), zeroFns);
    definitions.Add("voice-family", voiceFamily);
    var volume = new Property(
        1, Union(mozOutlineWidthLiterals0, volumeLiterals0), zeroFns);
    definitions.Add("volume", volume);
    var whiteSpace = new Property(
        0, Union(fontStyleLiterals0, whiteSpaceLiterals0), zeroFns);
    definitions.Add("white-space", whiteSpace);
    definitions.Add("word-wrap", new Property(0, wordWrapLiterals0, zeroFns));
    definitions.Add("zoom", new Property(1, fontStretchLiterals1, zeroFns));
    var rgbFun = new Property(1, rgbFunLiterals0, zeroFns);
    definitions.Add("rgb()", rgbFun);
    var imageFun = new Property(
        18, Union(mozOutlineLiterals0, rgbFunLiterals0), mozOutlineFunctions);
    definitions.Add("image()", imageFun);
    var linearGradientFun = new Property(
        7,
        Union(azimuthLiterals1, backgroundLiterals2,
              linearGradientFunLiterals0, mozOutlineLiterals0),
        mozOutlineFunctions);
    definitions.Add("linear-gradient()", linearGradientFun);
    var radialGradientFun = new Property(
        7,
        Union(azimuthLiterals1, backgroundLiterals2, mozOutlineLiterals0,
              radialGradientFunLiterals0, radialGradientFunLiterals1),
        mozOutlineFunctions);
    definitions.Add("radial-gradient()", radialGradientFun);
    definitions.Add("rect()", new Property(5, rectFunLiterals0, zeroFns));
    //definitions.Add("alpha()", new Property(1, alpha$FunLiterals0, zeroFns));
    definitions.Add("-moz-border-radius-bottomright", mozBorderRadiusBottomleft);
    definitions.Add("-moz-border-radius-topleft", mozBorderRadiusBottomleft);
    definitions.Add("-moz-border-radius-topright", mozBorderRadiusBottomleft);
    definitions.Add("-moz-box-shadow", boxShadow);
    definitions.Add("-webkit-border-bottom-left-radius", mozBorderRadiusBottomleft);
    definitions.Add("-webkit-border-bottom-right-radius",
                mozBorderRadiusBottomleft);
    definitions.Add("-webkit-border-radius", mozBorderRadius);
    definitions.Add("-webkit-border-radius-bottom-left", mozBorderRadiusBottomleft);
    definitions.Add("-webkit-border-radius-bottom-right",
                mozBorderRadiusBottomleft);
    definitions.Add("-webkit-border-radius-top-left", mozBorderRadiusBottomleft);
    definitions.Add("-webkit-border-radius-top-right", mozBorderRadiusBottomleft);
    definitions.Add("-webkit-border-top-left-radius", mozBorderRadiusBottomleft);
    definitions.Add("-webkit-border-top-right-radius", mozBorderRadiusBottomleft);
    definitions.Add("-webkit-box-shadow", boxShadow);
    definitions.Add("border-bottom", border);
    definitions.Add("border-bottom-left-radius", mozBorderRadiusBottomleft);
    definitions.Add("border-bottom-right-radius", mozBorderRadiusBottomleft);
    definitions.Add("border-bottom-style", mozOutlineStyle);
    definitions.Add("border-bottom-width", mozOutlineWidth);
    definitions.Add("border-color", borderBottomColor);
    definitions.Add("border-left", border);
    definitions.Add("border-left-color", borderBottomColor);
    definitions.Add("border-left-style", mozOutlineStyle);
    definitions.Add("border-left-width", mozOutlineWidth);
    definitions.Add("border-radius", mozBorderRadius);
    definitions.Add("border-right", border);
    definitions.Add("border-right-color", borderBottomColor);
    definitions.Add("border-right-style", mozOutlineStyle);
    definitions.Add("border-right-width", mozOutlineWidth);
    definitions.Add("border-style", mozOutlineStyle);
    definitions.Add("border-top", border);
    definitions.Add("border-top-color", borderBottomColor);
    definitions.Add("border-top-left-radius", mozBorderRadiusBottomleft);
    definitions.Add("border-top-right-radius", mozBorderRadiusBottomleft);
    definitions.Add("border-top-style", mozOutlineStyle);
    definitions.Add("border-top-width", mozOutlineWidth);
    definitions.Add("border-width", mozOutlineWidth);
    definitions.Add("cue-after", cue);
    definitions.Add("cue-before", cue);
    definitions.Add("left", height);
    definitions.Add("margin-bottom", margin);
    definitions.Add("margin-left", margin);
    definitions.Add("margin-right", margin);
    definitions.Add("margin-top", margin);
    definitions.Add("max-width", maxHeight);
    definitions.Add("min-height", margin);
    definitions.Add("min-width", margin);
    definitions.Add("outline", mozOutline);
    definitions.Add("outline-color", mozOutlineColor);
    definitions.Add("outline-style", mozOutlineStyle);
    definitions.Add("outline-width", mozOutlineWidth);
    definitions.Add("overflow-y", overflowX);
    definitions.Add("padding-bottom", padding);
    definitions.Add("padding-left", padding);
    definitions.Add("padding-right", padding);
    definitions.Add("padding-top", padding);
    definitions.Add("page-break-before", pageBreakAfter);
    definitions.Add("pause", borderSpacing);
    definitions.Add("pause-after", borderSpacing);
    definitions.Add("pause-before", borderSpacing);
    definitions.Add("pitch-range", borderSpacing);
    definitions.Add("richness", borderSpacing);
    definitions.Add("right", height);
    definitions.Add("stress", borderSpacing);
    definitions.Add("text-indent", borderSpacing);
    definitions.Add("text-overflow", oTextOverflow);
    definitions.Add("text-shadow", boxShadow);
    definitions.Add("top", height);
    definitions.Add("width", margin);
    definitions.Add("word-spacing", letterSpacing);
    definitions.Add("z-index", bottom);
    definitions.Add("rgba()", rgbFun);
    definitions.Add("repeating-linear-gradient()", linearGradientFun);
    definitions.Add("repeating-radial-gradient()", radialGradientFun);
    DEFINITIONS = definitions;
  }

  private static HashSet<T> Union<T>(params HashSet<T>[] subsets) {
    HashSet<T> all = new HashSet<T>();
    foreach (HashSet<T> subset in subsets) {
        foreach (T element in subset)
        {
            all.Add(element);
        }
    }
    return all;
  }

  internal static readonly HashSet<String> DEFAULT_WHITELIST = new HashSet<String>(new [] { 
      "-moz-border-radius",
      "-moz-border-radius-bottomleft",
      "-moz-border-radius-bottomright",
      "-moz-border-radius-topleft",
      "-moz-border-radius-topright",
      "-moz-box-shadow",
      "-moz-outline",
      "-moz-outline-color",
      "-moz-outline-style",
      "-moz-outline-width",
      "-o-text-overflow",
      "-webkit-border-bottom-left-radius",
      "-webkit-border-bottom-right-radius",
      "-webkit-border-radius",
      "-webkit-border-radius-bottom-left",
      "-webkit-border-radius-bottom-right",
      "-webkit-border-radius-top-left",
      "-webkit-border-radius-top-right",
      "-webkit-border-top-left-radius",
      "-webkit-border-top-right-radius",
      "-webkit-box-shadow",
      "azimuth",
      "background",
      "background-attachment",
      "background-color",
      "background-image",
      "background-position",
      "background-repeat",
      "border",
      "border-bottom",
      "border-bottom-color",
      "border-bottom-left-radius",
      "border-bottom-right-radius",
      "border-bottom-style",
      "border-bottom-width",
      "border-collapse",
      "border-color",
      "border-left",
      "border-left-color",
      "border-left-style",
      "border-left-width",
      "border-radius",
      "border-right",
      "border-right-color",
      "border-right-style",
      "border-right-width",
      "border-spacing",
      "border-style",
      "border-top",
      "border-top-color",
      "border-top-left-radius",
      "border-top-right-radius",
      "border-top-style",
      "border-top-width",
      "border-width",
      "box-shadow",
      "caption-side",
      "color",
      "cue",
      "cue-after",
      "cue-before",
      "direction",
      "elevation",
      "empty-cells",
      "font",
      "font-family",
      "font-size",
      "font-stretch",
      "font-style",
      "font-variant",
      "font-weight",
      "height",
      "image()",
      "letter-spacing",
      "line-height",
      "linear-gradient()",
      "list-style",
      "list-style-image",
      "list-style-position",
      "list-style-type",
      "margin",
      "margin-bottom",
      "margin-left",
      "margin-right",
      "margin-top",
      "max-height",
      "max-width",
      "min-height",
      "min-width",
      "outline",
      "outline-color",
      "outline-style",
      "outline-width",
      "padding",
      "padding-bottom",
      "padding-left",
      "padding-right",
      "padding-top",
      "pause",
      "pause-after",
      "pause-before",
      "pitch",
      "pitch-range",
      "quotes",
      "radial-gradient()",
      "rect()",
      "repeating-linear-gradient()",
      "repeating-radial-gradient()",
      "rgb()",
      "rgba()",
      "richness",
      "speak",
      "speak-header",
      "speak-numeral",
      "speak-punctuation",
      "speech-rate",
      "stress",
      "table-layout",
      "text-align",
      "text-decoration",
      "text-indent",
      "text-overflow",
      "text-shadow",
      "text-transform",
      "text-wrap",
      "unicode-bidi",
      "vertical-align",
      "voice-family",
      "volume",
      "white-space",
      "width",
      "word-spacing",
      "word-wrap"
  });

  /**
   * A schema that includes only those properties on the default schema
   * white-list.
   */
  public static readonly CssSchema DEFAULT =
      CssSchema.withProperties(DEFAULT_WHITELIST);

  /** Dumps key and literal list to stdout for easy examination. */
  public static void main(params String[] argv) {
    SortedSet<String> keys = new SortedSet<string>();
    SortedSet<String> literals = new SortedSet<string>();

    foreach (KeyValuePair<String, Property> e in DEFINITIONS) {
      keys.Add(e.Key);
        foreach (String literal in e.Value.literals)
        {
            literals.Add(literal);
        }
    }

    Console.WriteLine(
        "# Below two blocks of tokens.\n"
            + "#\n"
        + "# First are all property names.\n"
        + "# Those followed by an asterisk (*) are in the default white-list.\n"
        + "#\n"
        + "# Second are the literal tokens recognized in any defined property\n"
        + "# value.\n"
        );
    foreach (String key in keys) {
      Console.WriteLine(key);
      if (DEFAULT_WHITELIST.Contains(key)) { Console.WriteLine("*"); }
      Console.WriteLine();
    }
    Console.WriteLine();
    foreach (String literal in literals) {
        Console.WriteLine(literal);
    }
  }
}

}
