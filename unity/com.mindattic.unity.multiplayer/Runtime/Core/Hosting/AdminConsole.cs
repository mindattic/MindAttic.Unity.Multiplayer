using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.Multiplayer.Core
{
    /// <summary>What one admin command line produced.</summary>
    public readonly struct AdminCommandResult
    {
        /// <summary>Creates a result.</summary>
        public AdminCommandResult(bool succeeded, string output)
        {
            Succeeded = succeeded;
            Output = output ?? string.Empty;
        }

        /// <summary>False for an unknown command, bad arguments or a handler that threw.</summary>
        public bool Succeeded { get; }

        /// <summary>Text for the operator.</summary>
        public string Output { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return Output;
        }
    }

    /// <summary>
    /// The server's optional admin commands: lines typed on the server's standard input (<c>docker attach</c>, a
    /// terminal) when <see cref="ServerSettings.AdminCommands"/> is on. Engine-free: the runtime registers <c>status</c>,
    /// <c>players</c>, <c>kick</c>, <c>say</c> and <c>stop</c>; a game adds its own with <see cref="Register"/>.
    /// </summary>
    /// <remarks>Command names ignore case. Arguments split on spaces; double quotes keep spaces in one argument.</remarks>
    public sealed class AdminConsole
    {
        private readonly Dictionary<string, Command> _commands = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Creates a console with only <c>help</c>.</summary>
        public AdminConsole()
        {
            Register("help", "help", "Lists the commands.", args => Help());
        }

        /// <summary>The registered command names, sorted.</summary>
        public IReadOnlyList<string> Commands
        {
            get
            {
                var names = new List<string>(_commands.Keys);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names;
            }
        }

        /// <summary>Adds a command, replacing one with the same name.</summary>
        /// <param name="name">One word, no spaces.</param>
        /// <param name="usage">How to call it, for <c>help</c> (<c>kick &lt;clientId&gt; [reason]</c>).</param>
        /// <param name="description">What it does, in a sentence.</param>
        /// <param name="handler">Gets the arguments after the name; returns the text to show. Throw <see cref="ArgumentException"/> for bad arguments.</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty or has spaces.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
        public void Register(string name, string usage, string description, Func<IReadOnlyList<string>, string> handler)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf(' ') >= 0)
            {
                throw new ArgumentException("A command name is one word.", nameof(name));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _commands[name] = new Command(string.IsNullOrWhiteSpace(usage) ? name : usage, description ?? string.Empty, handler);
        }

        /// <summary>Removes a command. True when it existed.</summary>
        public bool Unregister(string name)
        {
            return name != null && _commands.Remove(name);
        }

        /// <summary>Runs one line. An empty line does nothing and succeeds.</summary>
        public AdminCommandResult Execute(string line)
        {
            IReadOnlyList<string> words = Split(line);
            if (words.Count == 0)
            {
                return new AdminCommandResult(true, string.Empty);
            }

            Command command;
            if (!_commands.TryGetValue(words[0], out command))
            {
                return new AdminCommandResult(false, "Unknown command '" + words[0] + "'. Type help for the list.");
            }

            var args = new List<string>(words.Count - 1);
            for (int i = 1; i < words.Count; i++)
            {
                args.Add(words[i]);
            }

            try
            {
                return new AdminCommandResult(true, command.Handler(args));
            }
            catch (ArgumentException ex)
            {
                return new AdminCommandResult(false, ex.Message + " Usage: " + command.Usage);
            }
            catch (Exception ex)
            {
                return new AdminCommandResult(false, words[0] + " failed: " + ex.Message);
            }
        }

        /// <summary>Splits a command line on whitespace; double quotes group words, <c>\"</c> is a literal quote.</summary>
        public static IReadOnlyList<string> Split(string line)
        {
            var words = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
            {
                return words;
            }

            var word = new StringBuilder();
            bool quoted = false;
            bool any = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    word.Append('"');
                    any = true;
                    i++;
                }
                else if (c == '"')
                {
                    quoted = !quoted;
                    any = true;
                }
                else if (!quoted && char.IsWhiteSpace(c))
                {
                    if (any)
                    {
                        words.Add(word.ToString());
                        word.Clear();
                        any = false;
                    }
                }
                else
                {
                    word.Append(c);
                    any = true;
                }
            }

            if (any)
            {
                words.Add(word.ToString());
            }

            return words;
        }

        private string Help()
        {
            var text = new StringBuilder("Commands:");
            foreach (string name in Commands)
            {
                Command command = _commands[name];
                text.Append("\n  ").Append(command.Usage);
                if (command.Description.Length > 0)
                {
                    text.Append("  - ").Append(command.Description);
                }
            }

            return text.ToString();
        }

        private sealed class Command
        {
            public Command(string usage, string description, Func<IReadOnlyList<string>, string> handler)
            {
                Usage = usage;
                Description = description;
                Handler = handler;
            }

            public string Usage { get; }

            public string Description { get; }

            public Func<IReadOnlyList<string>, string> Handler { get; }
        }
    }
}
