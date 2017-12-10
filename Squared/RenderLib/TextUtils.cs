﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Render.Text {
    public struct AbstractString : IEquatable<AbstractString> {
        private readonly string String;
        private readonly StringBuilder StringBuilder;
        private readonly ArraySegment<char> ArraySegment;

        public AbstractString (string text) {
            String = text;
            StringBuilder = null;
            ArraySegment = default(ArraySegment<char>);
        }

        public AbstractString (StringBuilder stringBuilder) {
            String = null;
            StringBuilder = stringBuilder;
            ArraySegment = default(ArraySegment<char>);
        }

        public AbstractString (char[] array) {
            String = null;
            StringBuilder = null;
            ArraySegment = new ArraySegment<char>(array);
        }

        public AbstractString (ArraySegment<char> array) {
            String = null;
            StringBuilder = null;
            ArraySegment = array;
        }

        public static implicit operator AbstractString (string text) {
            return new AbstractString(text);
        }

        public static implicit operator AbstractString (StringBuilder stringBuilder) {
            return new AbstractString(stringBuilder);
        }

        public static implicit operator AbstractString (char[] array) {
            return new AbstractString(array);
        }

        public static implicit operator AbstractString (ArraySegment<char> array) {
            return new AbstractString(array);
        }

        public bool Equals (AbstractString other) {
            return (String == other.String) &&
                (StringBuilder == other.StringBuilder) &&
                (ArraySegment == other.ArraySegment);
        }

        public char this[int index] {
            get {
                if (String != null)
                    return String[index];
                else if (StringBuilder != null)
                    return StringBuilder[index];
                else if (ArraySegment.Array != null) {
                    if ((index <= 0) || (index >= ArraySegment.Count))
                        throw new ArgumentOutOfRangeException("index");

                    return ArraySegment.Array[index + ArraySegment.Offset];
                } else
                    throw new NullReferenceException("This string contains no text");
            }
        }

        public int Length {
            get {
                if (String != null)
                    return String.Length;
                else if (StringBuilder != null)
                    return StringBuilder.Length;
                else // Default fallback to 0 characters
                    return ArraySegment.Count;
            }
        }

        public bool IsNull {
            get {
                return
                    (String == null) &&
                    (StringBuilder == null) &&
                    (ArraySegment.Array == null);
            }
        }

        public override string ToString () {
            if (String != null)
                return String;
            else if (StringBuilder != null)
                return StringBuilder.ToString();
            else if (ArraySegment.Array != null)
                return new string(ArraySegment.Array, ArraySegment.Offset, ArraySegment.Count);
            else
                throw new NullReferenceException("This string contains no text");
        }
    }

    public class DynamicStringLayout {
        private ArraySegment<BitmapDrawCall> _Buffer; 
        private StringLayout? _CachedStringLayout;
        private int _CachedGlyphVersion = -1;

        private Dictionary<char, KerningAdjustment> _KerningAdjustments; 
        private IGlyphSource _GlyphSource;
        private AbstractString _Text;
        private Vector2 _Position = Vector2.Zero;
        private Color _Color = Color.White;
        private float _Scale = 1;
        private float _SortKey = 0;
        private int _CharacterSkipCount = 0;
        private int _CharacterLimit = int.MaxValue;
        private float _XOffsetOfFirstLine = 0;
        private float _XOffsetOfNewLine = 0;
        private float? _LineBreakAtX = null;
        private bool _WordWrap = false;
        private bool _CharacterWrap = true;
        private float _WrapIndentation = 0f;
        private GlyphPixelAlignment _AlignToPixels = GlyphPixelAlignment.Default;
        private char _WrapCharacter = '\0';
        private int _Alignment = (int)HorizontalAlignment.Left;
        private bool _ReverseOrder = false;

        public DynamicStringLayout (SpriteFont font, string text = "") {
            _GlyphSource = new SpriteFontGlyphSource(font);
            _Text = text;
        }

        public DynamicStringLayout (IGlyphSource font = null, string text = "") {
            _GlyphSource = font;
            _Text = text;
        }

        private void InvalidatingNullableAssignment<T> (ref Nullable<T> destination, Nullable<T> newValue)
            where T : struct, IEquatable<T> {
            if (!destination.Equals(newValue)) {
                destination = newValue;
                _CachedStringLayout = null;
            }
        }

        private void InvalidatingValueAssignment<T> (ref T destination, T newValue) 
            where T : struct, IEquatable<T>
        {
            if (!destination.Equals(newValue)) {
                destination = newValue;
                _CachedStringLayout = null;
            }
        }

        private void InvalidatingReferenceAssignment<T> (ref T destination, T newValue)
            where T : class
        {
            if (destination != newValue) {
                destination = newValue;
                _CachedStringLayout = null;
            }
        }

        public ArraySegment<BitmapDrawCall> Buffer {
            get {
                return _Buffer;
            }
            set {
                _Buffer = value;
            }
        }

        public AbstractString Text {
            get {
                return _Text;
            }
            set {
                InvalidatingValueAssignment(ref _Text, value);
            }
        }

        public SpriteFont Font {
            get {
                if (_GlyphSource is SpriteFontGlyphSource)
                    return ((SpriteFontGlyphSource)_GlyphSource).Font;
                else
                    return null;
            }
            set {
                InvalidatingReferenceAssignment(
                    ref _GlyphSource, 
                    new SpriteFontGlyphSource(value)
                );
            }
        }

        public IGlyphSource GlyphSource {
            get {
                return _GlyphSource;
            }
            set {
                InvalidatingReferenceAssignment(ref _GlyphSource, value);
            }
        }

        public Vector2 Position {
            get {
                return _Position;
            }
            set {
                InvalidatingValueAssignment(ref _Position, value);
            }
        }

        public Color Color {
            get {
                return _Color;
            }
            set {
                InvalidatingValueAssignment(ref _Color, value);
            }
        }

        public float Scale {
            get {
                return _Scale;
            }
            set {
                InvalidatingValueAssignment(ref _Scale, value);
            }
        }

        public float SortKey {
            get {
                return _SortKey;
            }
            set {
                InvalidatingValueAssignment(ref _SortKey, value);
            }
        }

        public int CharacterSkipCount {
            get {
                return _CharacterSkipCount;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterSkipCount, value);
            }
        }

        public int CharacterLimit {
            get {
                return _CharacterLimit;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterLimit, value);
            }
        }

        public float XOffsetOfFirstLine {
            get {
                return _XOffsetOfFirstLine;
            }
            set {
                InvalidatingValueAssignment(ref _XOffsetOfFirstLine, value);
            }
        }

        public float XOffsetOfNewLine {
            get {
                return _XOffsetOfNewLine;
            }
            set {
                InvalidatingValueAssignment(ref _XOffsetOfNewLine, value);
            }
        }

        public float? LineBreakAtX {
            get {
                return _LineBreakAtX;
            }
            set {
                InvalidatingNullableAssignment(ref _LineBreakAtX, value);
            }
        }

        public bool WordWrap {
            get {
                return _WordWrap;
            }
            set {
                InvalidatingValueAssignment(ref _WordWrap, value);
            }
        }

        public bool CharacterWrap {
            get {
                // FIXME: Is this right?
                return _CharacterWrap;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterWrap, value);
            }
        }

        public HorizontalAlignment Alignment {
            get {
                return (HorizontalAlignment)_Alignment;
            }
            set {
                InvalidatingValueAssignment(ref _Alignment, (int)value);
            }
        }

        public char? WrapCharacter {
            get {
                return (_WrapCharacter == '\0') ? null : (char?)_WrapCharacter;
            }
            set {
                if (value.HasValue)
                    InvalidatingValueAssignment(ref _WrapCharacter, value.Value);
                else
                    InvalidatingValueAssignment(ref _WrapCharacter, '\0');
            }
        }

        /// <summary>
        /// NOTE: Only valid if WordWrap is also true
        /// </summary>
        public float WrapIndentation {
            get {
                return _WrapIndentation;
            }
            set {
                InvalidatingValueAssignment(ref _WrapIndentation, value);
            }
        }

        public GlyphPixelAlignment AlignToPixels {
            get {
                return _AlignToPixels;
            }
            set {
                InvalidatingValueAssignment(ref _AlignToPixels, value);
            }
        }

        public Dictionary<char, KerningAdjustment> KerningAdjustments {
            get {
                return _KerningAdjustments;
            }
            set {
                InvalidatingReferenceAssignment(ref _KerningAdjustments, value);
            }
        }

        public bool ReverseOrder {
            get {
                return _ReverseOrder;
            }
            set {
                InvalidatingValueAssignment(ref _ReverseOrder, value);
            }
        }

        public void Invalidate () {
            // Hey, you're the boss
            _CachedStringLayout = null;
        }

        public StringLayout Get () {
            if (_Text.IsNull)
                return new StringLayout();

            if (_CachedStringLayout.HasValue && _CachedGlyphVersion < _GlyphSource.Version)
                _CachedStringLayout = null;

            if (!_CachedStringLayout.HasValue) {
                int length = _Text.Length;

                int capacity = length + StringLayoutEngine.DefaultBufferPadding;

                if ((_Buffer.Array != null) && (_Buffer.Count < capacity))
                    _Buffer = default(ArraySegment<BitmapDrawCall>);

                if (_Buffer.Array == null) {
                    var newCapacity = 1 << (int)Math.Ceiling(Math.Log(capacity, 2));
                    var array = new BitmapDrawCall[newCapacity];
                    _Buffer = new ArraySegment<BitmapDrawCall>(array);
                }

                if (_Buffer.Count < capacity)
                    throw new InvalidOperationException("Buffer too small");

                using (
                    var le = new StringLayoutEngine {
                        buffer = _Buffer,
                        position = _Position,
                        color = _Color,
                        scale = _Scale,
                        sortKey = _SortKey,
                        characterSkipCount = _CharacterSkipCount,
                        characterLimit = _CharacterLimit,
                        xOffsetOfFirstLine = _XOffsetOfFirstLine,
                        xOffsetOfWrappedLine = _XOffsetOfNewLine + _WrapIndentation,
                        xOffsetOfNewLine = _XOffsetOfNewLine,
                        lineBreakAtX = _LineBreakAtX,
                        alignToPixels = _AlignToPixels,
                        characterWrap = _CharacterWrap,
                        wordWrap = _WordWrap,
                        wrapCharacter = _WrapCharacter,
                        alignment = (HorizontalAlignment)_Alignment,
                        reverseOrder = _ReverseOrder
                    }
                ) {
                    le.Initialize();
                    le.AppendText(_GlyphSource, _Text, _KerningAdjustments);

                    _CachedGlyphVersion = _GlyphSource.Version;
                    _CachedStringLayout = le.Finish();
                }
            }

            return _CachedStringLayout.Value;
        }
    }

    public class FallbackGlyphSource : IGlyphSource, IDisposable {
        public readonly List<IGlyphSource> Sources = new List<IGlyphSource>();

        public FallbackGlyphSource (params IGlyphSource[] sources) {
            Sources.AddRange(sources);
        }

        public SpriteFont SpriteFont
        {
            get
            {
                return null;
            }
        }

        public bool GetGlyph (char ch, out Glyph result) {
            foreach (var item in Sources) {
                if (item.GetGlyph(ch, out result))
                    return true;
            }

            result = default(Glyph);
            return false;
        }

        public float DPIScaleFactor {
            get {
                return Sources[0].DPIScaleFactor;
            }
        }

        public float LineSpacing {
            get {
                return Sources[0].LineSpacing;
            }
        }

        int IGlyphSource.Version {
            get {
                int result = 0;
                foreach (var item in Sources)
                    result += item.Version;
                return result;
            }
        }
        
        public void Dispose () {
            foreach (var item in Sources) {
                if (item is IDisposable)
                    ((IDisposable)item).Dispose();
            }
        }
    }

    public interface IGlyphSource {
        bool GetGlyph (char ch, out Glyph result);
        float LineSpacing { get; }
        float DPIScaleFactor { get; }

        int Version { get; }
    }

    public static class SpriteFontUtil {
        public struct FontFields {
            public Texture2D Texture;
            public List<Rectangle> GlyphRectangles;
            public List<Rectangle> CropRectangles;
            public List<char> Characters;
            public List<Vector3> Kerning;
        }
        internal static readonly FieldInfo textureValue, glyphData, croppingData, kerning, characterMap;

        static SpriteFontUtil () {
            var tSpriteFont = typeof(SpriteFont);
#if MG // MonoGame
            // Only textureValue is used (and only to pass the "compatibility" check)
            textureValue = GetPrivateField( tSpriteFont, "_texture" );
            glyphData = null;
            croppingData = null;
            kerning = null;
            characterMap = null;
#else // XNA
            textureValue = GetPrivateField(tSpriteFont, "textureValue");
            glyphData = GetPrivateField(tSpriteFont, "glyphData");
            croppingData = GetPrivateField(tSpriteFont, "croppingData");
            kerning = GetPrivateField(tSpriteFont, "kerning");
            characterMap = GetPrivateField(tSpriteFont, "characterMap");
#endif
        }

        private static FieldInfo GetPrivateField (Type type, string fieldName) {
            return type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public static bool GetPrivateFields (this SpriteFont font, out FontFields result) {
            if (textureValue == null) {
                result = default(FontFields);
                return false;
            }

#if MG // MonoGame -- Has public access to necessary information (albeit a bit clumsily)
            Dictionary<char, SpriteFont.Glyph> glyphDict = font.GetGlyphs();
            List<SpriteFont.Glyph> glyphs = glyphDict.Values.ToList();
            result = new FontFields {
                Texture = font.Texture,
                GlyphRectangles = glyphs.Select( g => g.BoundsInTexture ).ToList(),
                CropRectangles = glyphs.Select( g => g.Cropping ).ToList(),
                Characters = glyphs.Select( g => g.Character ).ToList(),
                Kerning = glyphs.Select( g => new Vector3( g.LeftSideBearing, g.Width, g.RightSideBearing ) ).ToList()
            };
#else // XNA -- Needs access to a bunch of private fields
            result = new FontFields {
                Texture = (Texture2D)(textureValue).GetValue(font),
                GlyphRectangles = (List<Rectangle>)glyphData.GetValue(font),
                CropRectangles = (List<Rectangle>)croppingData.GetValue(font),
                Characters = (List<char>)characterMap.GetValue(font),
                Kerning = (List<Vector3>)kerning.GetValue(font)
            };
#endif
            return true;
        }
    }

    public struct SpriteFontGlyphSource : IGlyphSource {
        public readonly SpriteFont Font;
        public readonly Texture2D Texture;

        public readonly SpriteFontUtil.FontFields Fields;
        public readonly int DefaultCharacterIndex;

        // Forward some SpriteFont methods and properties to make it easier to drop-in replace
        
        public float Spacing {
            get {
                return Font.Spacing;
            }
        }

        public float LineSpacing {
            get {
                return Font.LineSpacing;
            }
        }

        int IGlyphSource.Version {
            get {
                return 1;
            }
        }

        public Vector2 MeasureString (string text) {
            return Font.MeasureString(text);
        }

        public Vector2 MeasureString (StringBuilder text) {
            return Font.MeasureString(text);
        }


        public float DPIScaleFactor {
            get {
                return 1.0f;
            }
        }

        private void MakeGlyphForCharacter (char ch, int characterIndex, out Glyph glyph) {
            var kerning = Fields.Kerning[characterIndex];
            var cropping = Fields.CropRectangles[characterIndex];

            glyph = new Glyph {
                Character = ch,
                Texture = Texture,
                BoundsInTexture = Fields.GlyphRectangles[characterIndex],
                XOffset = cropping.X,
                YOffset = cropping.Y,
                LeftSideBearing = kerning.X,
                RightSideBearing = kerning.Z,
                Width = kerning.Y,
                CharacterSpacing = Font.Spacing,
                LineSpacing = Font.LineSpacing
            };
        }

        public bool GetGlyph (char ch, out Glyph result) {
            var characterIndex = Fields.Characters.BinarySearch(ch);
            if (characterIndex < 0)
                characterIndex = DefaultCharacterIndex;

            if (characterIndex < 0) {
                result = default(Glyph);
                return false;
            }

            MakeGlyphForCharacter(ch, characterIndex, out result);
            return true;
        }

        public SpriteFontGlyphSource (SpriteFont font) {
            Font = font;

            if (SpriteFontUtil.GetPrivateFields(font, out Fields)) {
                // XNA SpriteFont
                Texture = Fields.Texture;

                if (Font.DefaultCharacter.HasValue)
                    DefaultCharacterIndex = Fields.Characters.BinarySearch(Font.DefaultCharacter.Value);
                else
                    DefaultCharacterIndex = -1;
            } else {
                throw new NotImplementedException("Unsupported SpriteFont implementation");
            }
        }
    }

    public struct Glyph {
        public Texture2D Texture;
        public char Character;
        public Rectangle BoundsInTexture;
        public float XOffset, YOffset;
        public float LeftSideBearing;
        public float RightSideBearing;
        public float Width;
        public float CharacterSpacing;
        public float LineSpacing;
    }
}
