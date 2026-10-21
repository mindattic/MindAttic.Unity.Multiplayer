namespace Unity.Multiplayer.Core
{
    /// <summary>Why player input did not parse as a join code.</summary>
    public enum JoinCodeError
    {
        /// <summary>It parsed.</summary>
        None = 0,

        /// <summary>Nothing but whitespace and separators.</summary>
        Empty = 1,

        /// <summary>Fewer characters than the shortest code.</summary>
        TooShort = 2,

        /// <summary>More characters than the longest code.</summary>
        TooLong = 3,

        /// <summary>A character outside the alphabet that could not be corrected; see <see cref="JoinCodeParseResult.Position"/>.</summary>
        InvalidCharacter = 4,

        /// <summary>Every character is valid but the check character does not match: a typo or a swap.</summary>
        ChecksumMismatch = 5,
    }

    /// <summary>The outcome of <see cref="JoinCode.TryParse(string, out JoinCodeParseResult)"/>.</summary>
    public readonly struct JoinCodeParseResult
    {
        private JoinCodeParseResult(JoinCode code, JoinCodeError error, int position, bool corrected, string message)
        {
            Code = code;
            Error = error;
            Position = position;
            Corrected = corrected;
            Message = message;
        }

        /// <summary>True when the input parsed.</summary>
        public bool Success
        {
            get { return Error == JoinCodeError.None; }
        }

        /// <summary>The parsed code; <see cref="JoinCode.Empty"/> on failure.</summary>
        public JoinCode Code { get; }

        /// <summary>Why parsing failed, or <see cref="JoinCodeError.None"/>.</summary>
        public JoinCodeError Error { get; }

        /// <summary>Zero-based index, in the normalized input, of the offending character; -1 when no single character is to blame.</summary>
        public int Position { get; }

        /// <summary>True when the input held a look-alike (0, O, 1 or I) that the check character resolved.</summary>
        public bool Corrected { get; }

        /// <summary>A sentence a game can show the player; empty on success.</summary>
        public string Message { get; }

        internal static JoinCodeParseResult Ok(JoinCode code, bool corrected)
        {
            return new JoinCodeParseResult(code, JoinCodeError.None, -1, corrected, string.Empty);
        }

        internal static JoinCodeParseResult Fail(JoinCodeError error, int position, string message)
        {
            return new JoinCodeParseResult(JoinCode.Empty, error, position, false, message);
        }
    }
}
