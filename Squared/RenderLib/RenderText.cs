﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Squared.Render.Evil;
using Squared.Render.Text;
using Squared.Util;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace Squared.Render.Text {
    public struct StringLayout {
        private static readonly ConditionalWeakTable<SpriteFont, Dictionary<char, KerningAdjustment>> _DefaultKerningAdjustments =
            new ConditionalWeakTable<SpriteFont, Dictionary<char, KerningAdjustment>>(); 

        public readonly Vector2 Position;
        public readonly Vector2 Size;
        public readonly float LineHeight;
        public readonly Bounds FirstCharacterBounds;
        public readonly Bounds LastCharacterBounds;
        public readonly ArraySegment<BitmapDrawCall> DrawCalls;

        public StringLayout (Vector2 position, Vector2 size, float lineHeight, Bounds firstCharacter, Bounds lastCharacter, ArraySegment<BitmapDrawCall> drawCalls) {
            Position = position;
            Size = size;
            LineHeight = lineHeight;
            FirstCharacterBounds = firstCharacter;
            LastCharacterBounds = lastCharacter;
            DrawCalls = drawCalls;
        }

        public int Count {
            get {
                return DrawCalls.Count;
            }
        }

        public BitmapDrawCall this[int index] {
            get {
                if ((index < 0) || (index >= Count))
                    throw new ArgumentOutOfRangeException("index");

                return DrawCalls.Array[DrawCalls.Offset + index];
            }
            set {
                if ((index < 0) || (index >= Count))
                    throw new ArgumentOutOfRangeException("index");

                DrawCalls.Array[DrawCalls.Offset + index] = value;
            }
        }

        public ArraySegment<BitmapDrawCall> Slice (int skip, int count) {
            return new ArraySegment<BitmapDrawCall>(
                DrawCalls.Array, DrawCalls.Offset + skip, Math.Max(Math.Min(count, DrawCalls.Count - skip), 0)
            );
        }

        public static implicit operator ArraySegment<BitmapDrawCall> (StringLayout layout) {
            return layout.DrawCalls;
        }

        public static Dictionary<char, KerningAdjustment> GetDefaultKerningAdjustments (IGlyphSource font) {
            // FIXME
            if (font is SpriteFontGlyphSource) {
                Dictionary<char, KerningAdjustment> result;
                _DefaultKerningAdjustments.TryGetValue(((SpriteFontGlyphSource)font).Font, out result);
                return result;
            } else {
                return null;
            }
        }

        public static void SetDefaultKerningAdjustments (SpriteFont font, Dictionary<char, KerningAdjustment> adjustments) {
            _DefaultKerningAdjustments.Remove(font);
            _DefaultKerningAdjustments.Add(font, adjustments);
        }
    }

    public struct KerningAdjustment {
        public float LeftSideBearing, RightSideBearing, Width;

        public KerningAdjustment (float leftSide = 0f, float rightSide = 0f, float width = 0f) {
            LeftSideBearing = leftSide;
            RightSideBearing = rightSide;
            Width = width;
        }
    }

    public enum HorizontalAlignment : int {
        Left,
        Center,
        Right,
        // Justify
    }

    public struct StringLayoutEngine : IDisposable {
        public const int DefaultBufferPadding = 64;

        // Parameters
        public ArraySegment<BitmapDrawCall> buffer;
        public Vector2? position;
        public Color?   color;
        public float    scale;
        public DrawCallSortKey sortKey;
        public int      characterSkipCount;
        public int?     characterLimit;
        public float    xOffsetOfFirstLine;
        public float    xOffsetOfWrappedLine;
        public float    xOffsetOfNewLine;
        public float?   lineBreakAtX;
        public bool     characterWrap;
        public bool     wordWrap;
        public char     wrapCharacter;
        public bool     reverseOrder;
        public GlyphPixelAlignment alignToPixels;
        public HorizontalAlignment alignment;
        public Func<ArraySegment<BitmapDrawCall>, ArraySegment<BitmapDrawCall>> growBuffer;

        // State
        public float    maxLineHeight;
        public Vector2  actualPosition, characterOffset;
        public Bounds   firstCharacterBounds, lastCharacterBounds;
        public int      drawCallsWritten;
        float   initialLineXOffset;
        int     bufferWritePosition, wordStartWritePosition;
        int     rowIndex, colIndex;
        bool    wordWrapSuppressed;
        float   currentLineMaxX;
        float   currentLineWhitespaceMaxXLeft, currentLineWhitespaceMaxX;
        float   maxX, maxY;
        float?  currentLineSpacing;
        float   maxLineSpacing;
        Vector2 wordStartOffset;
        private bool ownsBuffer;

        private bool IsInitialized;

        public void Initialize () {
            actualPosition = position.GetValueOrDefault(Vector2.Zero);
            characterOffset = new Vector2(xOffsetOfFirstLine, 0);
            initialLineXOffset = characterOffset.X;

            bufferWritePosition = 0;
            wordStartWritePosition = -1;
            wordStartOffset = Vector2.Zero;
            rowIndex = colIndex = 0;
            wordWrapSuppressed = false;
            currentLineSpacing = null;
            maxLineSpacing = 0;
            alignToPixels = GlyphPixelAlignment.Default;

            IsInitialized = true;
        }

        private void WrapWord (
            ArraySegment<BitmapDrawCall> buffer,
            Vector2 firstOffset, int firstIndex, int lastIndex, float effectiveScale, float effectiveLineSpacing
        ) {
            // FIXME: Can this ever happen?
            if (currentLineWhitespaceMaxX <= 0)
                maxX = Math.Max(maxX, currentLineMaxX * effectiveScale);
            else
                maxX = Math.Max(maxX, currentLineWhitespaceMaxXLeft * effectiveScale);

            var scaledFirstOffset = firstOffset * effectiveScale;
            var scaledLineSpacing = currentLineSpacing.GetValueOrDefault(maxLineSpacing) * effectiveScale;

            for (var i = firstIndex; i <= lastIndex; i++) {
                var dc = buffer.Array[buffer.Offset + i];
                var newCharacterX = (xOffsetOfWrappedLine * effectiveScale) + (dc.Position.X - scaledFirstOffset.X);

                dc.Position = new Vector2(newCharacterX, dc.Position.Y + scaledLineSpacing);
                if (alignment != HorizontalAlignment.Left)
                    dc.SortKey.Order += 1;

                buffer.Array[buffer.Offset + i] = dc;
            }

            characterOffset.X = xOffsetOfWrappedLine + (characterOffset.X - firstOffset.X);
            characterOffset.Y += effectiveLineSpacing;

            // HACK: firstOffset may include whitespace so we want to pull the right edge in.
            //  Without doing this, the size rect for the string is too large.
            var actualRightEdge = firstOffset.X;
            if (firstIndex > 0)
                actualRightEdge = Math.Min(
                    actualRightEdge, 
                    buffer.Array[buffer.Offset + firstIndex - 1].EstimateDrawBounds().BottomRight.X
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Snap (Vector2 pos, out Vector2 result) {
            float x, y;

            switch (alignToPixels.Horizontal) {
                case PixelAlignmentMode.Floor:
                    x = (float)Math.Floor(pos.X);
                    break;
                case PixelAlignmentMode.FloorHalf:
                    x = (float)Math.Floor(pos.X * 2) / 2;
                    break;
                default:
                    x = pos.X;
                    break;
            }

            switch (alignToPixels.Vertical) {
                case PixelAlignmentMode.Floor:
                    y = (float)Math.Floor(pos.Y);
                    break;
                case PixelAlignmentMode.FloorHalf:
                    y = (float)Math.Floor(pos.Y * 2) / 2;
                    break;
                default:
                    y = pos.Y;
                    break;
            }

            result = new Vector2(x, y);
        }

        private void AlignLine (
            ArraySegment<BitmapDrawCall> buffer, HorizontalAlignment alignment,
            int firstIndex, int lastIndex
        ) {
            var firstDc = buffer.Array[buffer.Offset + firstIndex].EstimateDrawBounds();
            var endDc = buffer.Array[buffer.Offset + lastIndex].EstimateDrawBounds();
            var lineWidth = (endDc.BottomRight.X - firstDc.TopLeft.X);

            float whitespace;
            if (lineBreakAtX.HasValue)
                whitespace = lineBreakAtX.Value - lineWidth;
            else
                whitespace = maxX - lineWidth;

            // HACK: Don't do anything if the line is too big, just overflow to the right.
            //  Otherwise, the sizing info will be wrong and bad things happen.
            if (whitespace <= 0)
                whitespace = 0;

            // HACK: We compute this before halving the whitespace, so that the size of 
            //  the layout is enough to ensure manually centering the whole layout will
            //  still preserve per-line centering.
            maxX = Math.Max(maxX, whitespace + lineWidth);

            if (alignment == HorizontalAlignment.Center)
                whitespace /= 2;

            for (var j = firstIndex; j <= lastIndex; j++) {
                buffer.Array[buffer.Offset + j].Position.X += whitespace;
                // We used the sortkey to store line numbers, now we put the right data there
                buffer.Array[buffer.Offset + j].SortKey = sortKey;
            }
        }

        private void AlignLines (
            ArraySegment<BitmapDrawCall> buffer, HorizontalAlignment alignment
        ) {
            if (alignment == HorizontalAlignment.Left)
                return;
            if (buffer.Count == 0)
                return;

            int lineStartIndex = 0;
            float currentLine = buffer.Array[buffer.Offset].SortKey.Order;

            for (var i = 1; i < buffer.Count; i++) {
                var line = buffer.Array[buffer.Offset + i].SortKey.Order;

                if (line > currentLine) {
                    AlignLine(buffer, alignment, lineStartIndex, i - 1);

                    lineStartIndex = i;
                    currentLine = line;
                }
            }

            AlignLine(buffer, alignment, lineStartIndex, buffer.Count - 1);
        }

        private void EnsureBufferCapacity (int count) {
            int paddedCount = count + DefaultBufferPadding;

            if (buffer.Array == null) {
                ownsBuffer = true;
                buffer = new ArraySegment<BitmapDrawCall>(
                    new BitmapDrawCall[paddedCount]
                );
            } else if (buffer.Count < paddedCount) {
                if (ownsBuffer) {
                    var oldBuffer = buffer;
                    var newSize = Math.Min(paddedCount + 256, oldBuffer.Count * 2);
                    buffer = new ArraySegment<BitmapDrawCall>(
                        new BitmapDrawCall[newSize]
                    );
                    Array.Copy(oldBuffer.Array, buffer.Array, oldBuffer.Count);
                } else if (buffer.Count >= count) {
                    // This is OK, there should be enough room...
                } else {
                    throw new InvalidOperationException("Buffer too small");
                }
            }
        }

        public ArraySegment<BitmapDrawCall> AppendText (
            IGlyphSource font, AbstractString text,
            Dictionary<char, KerningAdjustment> kerningAdjustments = null
        ) {
            if (!IsInitialized)
                throw new InvalidOperationException("Call Initialize first");

            if (font == null)
                throw new ArgumentNullException("font");
            if (text.IsNull)
                throw new ArgumentNullException("text");

            EnsureBufferCapacity(bufferWritePosition + text.Length);

            if (kerningAdjustments == null)
                kerningAdjustments = StringLayout.GetDefaultKerningAdjustments(font);

            var effectiveScale = scale / font.DPIScaleFactor;

            var drawCall = default(BitmapDrawCall);
            drawCall.MultiplyColor = color.GetValueOrDefault(Color.White);
            drawCall.ScaleF = effectiveScale;
            drawCall.SortKey = sortKey;

            float x = 0;
            float? defaultLineSpacing = null;

            for (int i = 0, l = text.Length; i < l; i++) {
                var ch = text[i];
                bool isWhiteSpace = char.IsWhiteSpace(ch),
                     forcedWrap = false, lineBreak = false,
                     deadGlyph = false;
                Glyph glyph;
                KerningAdjustment kerningAdjustment;

                if (ch == '\r') {
                    if (((i + 1) < l) && (text[i + 1] == '\n'))
                        i += 1;

                    lineBreak = true;
                } else if (ch == '\n') {
                    lineBreak = true;
                }

                if (isWhiteSpace) {
                    wordStartWritePosition = -1;
                    wordWrapSuppressed = false;
                } else {
                    if (wordStartWritePosition < 0) {
                        wordStartWritePosition = bufferWritePosition;
                        wordStartOffset = characterOffset;
                    }
                }

                deadGlyph = !font.GetGlyph(ch, out glyph);

                float effectiveLineSpacing = glyph.LineSpacing;
                if (deadGlyph) {
                    if (currentLineSpacing.HasValue)
                        effectiveLineSpacing = currentLineSpacing.Value;
                    else if (defaultLineSpacing.HasValue)
                        effectiveLineSpacing = defaultLineSpacing.Value;
                    else {
                        Glyph space;
                        if (font.GetGlyph(' ', out space))
                            defaultLineSpacing = effectiveLineSpacing = space.LineSpacing;
                    }
                }

                if ((kerningAdjustments != null) && kerningAdjustments.TryGetValue(ch, out kerningAdjustment)) {
                    glyph.LeftSideBearing += kerningAdjustment.LeftSideBearing;
                    glyph.Width += kerningAdjustment.Width;
                    glyph.RightSideBearing += kerningAdjustment.RightSideBearing;
                }

                x = 
                    characterOffset.X + 
                    glyph.LeftSideBearing + 
                    glyph.RightSideBearing + 
                    glyph.Width + glyph.CharacterSpacing;

                if ((x * effectiveScale) >= lineBreakAtX) {
                    if (
                        !deadGlyph &&
                        (colIndex > 0) &&
                        !isWhiteSpace
                    )
                        forcedWrap = true;
                }

                if (forcedWrap) {
                    var currentWordSize = x - wordStartOffset.X;

                    if (wordWrap && !wordWrapSuppressed && (currentWordSize * effectiveScale <= lineBreakAtX)) {
                        WrapWord(buffer, wordStartOffset, wordStartWritePosition, bufferWritePosition - 1, effectiveScale, effectiveLineSpacing);
                        wordWrapSuppressed = true;
                        lineBreak = true;
                    } else if (characterWrap) {
                        characterOffset.X = xOffsetOfWrappedLine;
                        characterOffset.Y += effectiveLineSpacing;

                        maxX = Math.Max(maxX, currentLineMaxX * effectiveScale);
                        wordStartWritePosition = bufferWritePosition;
                        wordStartOffset = characterOffset;
                        lineBreak = true;
                    }
                }

                if (lineBreak) {
                    if (!forcedWrap) {
                        characterOffset.X = xOffsetOfNewLine;
                        characterOffset.Y += effectiveLineSpacing;
                        maxX = Math.Max(maxX, currentLineMaxX * effectiveScale);
                    }

                    initialLineXOffset = characterOffset.X;
                    currentLineMaxX = 0;
                    currentLineWhitespaceMaxX = 0;
                    currentLineWhitespaceMaxXLeft = 0;
                    rowIndex += 1;
                    colIndex = 0;
                }

                if (deadGlyph) {
                    characterSkipCount--;
                    if (characterLimit.HasValue)
                        characterLimit--;
                    continue;
                }

                // HACK: Recompute after wrapping
                x = 
                    characterOffset.X + 
                    glyph.LeftSideBearing + 
                    glyph.RightSideBearing + 
                    glyph.Width + glyph.CharacterSpacing;

                characterOffset.X += glyph.CharacterSpacing;

                lastCharacterBounds = Bounds.FromPositionAndSize(
                    characterOffset * effectiveScale, new Vector2(
                        glyph.LeftSideBearing + glyph.Width + glyph.RightSideBearing,
                        glyph.LineSpacing
                    ) * effectiveScale
                );

                if ((rowIndex == 0) && (colIndex == 0))
                    firstCharacterBounds = lastCharacterBounds;

                characterOffset.X += glyph.LeftSideBearing;

                if (colIndex == 0)
                    characterOffset.X = Math.Max(characterOffset.X, 0);

                if (characterSkipCount <= 0) {
                    if (characterLimit.HasValue && characterLimit.Value <= 0)
                        break;

                    var glyphPosition = new Vector2(
                        actualPosition.X + (glyph.XOffset + characterOffset.X) * effectiveScale,
                        actualPosition.Y + (glyph.YOffset + characterOffset.Y) * effectiveScale
                    );

                    if (!isWhiteSpace) {                    
                        if (bufferWritePosition >= buffer.Count)
                            EnsureBufferCapacity(bufferWritePosition);

                        drawCall.Texture = glyph.Texture;
                        drawCall.TextureRegion = glyph.Texture.BoundsFromRectangle(ref glyph.BoundsInTexture);
                        Snap(glyphPosition, out drawCall.Position);

                        // HACK so that the alignment pass can detect rows. We strip this later.
                        if (alignment != HorizontalAlignment.Left)
                            drawCall.SortKey.Order = rowIndex;

                        buffer.Array[buffer.Offset + bufferWritePosition] = drawCall;

                        currentLineMaxX = Math.Max(currentLineMaxX, x);
                        maxY = Math.Max(maxY, (characterOffset.Y + effectiveLineSpacing) * effectiveScale);

                        bufferWritePosition += 1;
                        drawCallsWritten += 1;
                    } else {
                        currentLineWhitespaceMaxXLeft = Math.Max(currentLineWhitespaceMaxXLeft, characterOffset.X);
                        currentLineWhitespaceMaxX = Math.Max(currentLineWhitespaceMaxX, x);
                    }

                    characterLimit--;
                } else {
                    characterSkipCount--;
                }

                characterOffset.X += (glyph.Width + glyph.RightSideBearing);
                currentLineSpacing = glyph.LineSpacing;
                maxLineSpacing = Math.Max(maxLineSpacing, effectiveLineSpacing);

                colIndex += 1;
            }

            var segment = new ArraySegment<BitmapDrawCall>(
                buffer.Array, buffer.Offset, drawCallsWritten
            );

            maxX = Math.Max(maxX, currentLineMaxX * effectiveScale);

            return segment;
        }

        public StringLayout Finish () {
            ArraySegment<BitmapDrawCall> result;
            if (buffer.Array == null)
                result = default(ArraySegment<BitmapDrawCall>);
            else
                result = new ArraySegment<BitmapDrawCall>(
                    buffer.Array, buffer.Offset, drawCallsWritten
                );

            if (alignment != HorizontalAlignment.Left)
                AlignLines(result, alignment);

            if (reverseOrder)
                Array.Reverse(result.Array, result.Offset, result.Count);

            return new StringLayout(
                position.GetValueOrDefault(), 
                new Vector2(maxX, maxY),
                maxLineSpacing,
                firstCharacterBounds, lastCharacterBounds,
                result
            );
        }

        public void Dispose () {
        }
    }
}

namespace Squared.Render {
    public static class SpriteFontExtensions {
        public static StringLayout LayoutString (
            this SpriteFont font, AbstractString text, ArraySegment<BitmapDrawCall>? buffer = null,
            Vector2? position = null, Color? color = null, float scale = 1, 
            DrawCallSortKey sortKey = default(DrawCallSortKey),
            int characterSkipCount = 0, int? characterLimit = null,
            float xOffsetOfFirstLine = 0, float? lineBreakAtX = null,
            GlyphPixelAlignment alignToPixels = default(GlyphPixelAlignment),
            Dictionary<char, KerningAdjustment> kerningAdjustments = null,
            bool wordWrap = false, char wrapCharacter = '\0'
        ) {
            var state = new StringLayoutEngine {
                position = position,
                color = color,
                scale = scale,
                sortKey = sortKey,
                characterSkipCount = characterSkipCount,
                characterLimit = characterLimit,
                xOffsetOfFirstLine = xOffsetOfFirstLine,
                lineBreakAtX = lineBreakAtX,
                alignToPixels = alignToPixels,
                characterWrap = lineBreakAtX.HasValue,
                wordWrap = wordWrap,
                wrapCharacter = wrapCharacter,
                buffer = buffer.GetValueOrDefault(default(ArraySegment<BitmapDrawCall>))
            };
            var gs = new SpriteFontGlyphSource(font);

            state.Initialize();

            using (state) {
                var segment = state.AppendText(
                    gs, text, kerningAdjustments
                );

                return state.Finish();
            }
        }

        // Yuck :(
        public static StringLayout LayoutString (
            this IGlyphSource glyphSource, AbstractString text, ArraySegment<BitmapDrawCall>? buffer = null,
            Vector2? position = null, Color? color = null, float scale = 1, 
            DrawCallSortKey sortKey = default(DrawCallSortKey),
            int characterSkipCount = 0, int? characterLimit = null,
            float xOffsetOfFirstLine = 0, float? lineBreakAtX = null,
            bool alignToPixels = false,
            Dictionary<char, KerningAdjustment> kerningAdjustments = null,
            bool wordWrap = false, char wrapCharacter = '\0'
        ) {
            var state = new StringLayoutEngine {
                position = position,
                color = color,
                scale = scale,
                sortKey = sortKey,
                characterSkipCount = characterSkipCount,
                characterLimit = characterLimit,
                xOffsetOfFirstLine = xOffsetOfFirstLine,
                lineBreakAtX = lineBreakAtX,
                alignToPixels = alignToPixels,
                characterWrap = lineBreakAtX.HasValue,
                wordWrap = wordWrap,
                wrapCharacter = wrapCharacter,
                buffer = buffer.GetValueOrDefault(default(ArraySegment<BitmapDrawCall>))
            };

            state.Initialize();

            using (state) {
                var segment = state.AppendText(
                    glyphSource, text, kerningAdjustments
                );

                return state.Finish();
            }
        }
    }

    namespace Text {
        public enum PixelAlignmentMode {
            None,
            Floor,
            // Like Floor but allows half-pixel values (x.5 in addition to x.0)
            FloorHalf
        }

        public struct GlyphPixelAlignment : IEquatable<GlyphPixelAlignment> {
            public PixelAlignmentMode Horizontal, Vertical;

            public GlyphPixelAlignment (bool alignToPixels) {
                Horizontal = Vertical = alignToPixels ? PixelAlignmentMode.Floor : PixelAlignmentMode.None;
            }

            public GlyphPixelAlignment (PixelAlignmentMode mode) {
                Horizontal = Vertical = mode;
            }

            public GlyphPixelAlignment (PixelAlignmentMode horizontal, PixelAlignmentMode vertical) {
                Horizontal = horizontal;
                Vertical = vertical;
            }

            public static implicit operator GlyphPixelAlignment (bool alignToPixels) {
                return new GlyphPixelAlignment(alignToPixels);
            }

            public static readonly GlyphPixelAlignment Default = new GlyphPixelAlignment(PixelAlignmentMode.None);
            public static readonly GlyphPixelAlignment FloorXY = new GlyphPixelAlignment(PixelAlignmentMode.Floor);
            public static readonly GlyphPixelAlignment FloorY = new GlyphPixelAlignment(PixelAlignmentMode.None, PixelAlignmentMode.Floor);

            public bool Equals (GlyphPixelAlignment other) {
                return (other.Horizontal == Horizontal) && (other.Vertical == Vertical);
            }

            public override bool Equals (object obj) {
                if (obj is GlyphPixelAlignment)
                    return Equals((GlyphPixelAlignment)obj);

                return false;
            }

            public override string ToString () {
                if (Horizontal == Vertical)
                    return Horizontal.ToString();
                else
                    return string.Format("{0}, {1}", Horizontal, Vertical);
            }
        }
    }
}