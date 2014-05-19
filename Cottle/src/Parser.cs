﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Cottle.Exceptions;
using Cottle.Expressions;
using Cottle.Nodes;
using Cottle.Parsers.Default;

namespace Cottle
{
	class Parser
	{
		#region Attributes / Instance

		private readonly Lexer		lexer;

		private readonly Trimmer	trimmer;

		#endregion

		#region Attributes / Static

		private static readonly Dictionary<string, KeywordDelegate> keywords = new Dictionary<string, KeywordDelegate>
		{
			{"_",		(p) => p.ParseKeywordComment ()},
			{"declare",	(p) => p.ParseKeywordDeclare ()},
			{"define",	(p) => p.ParseKeywordSet ()},
			{"dump",	(p) => p.ParseKeywordDump ()},
			{"echo",	(p) => p.ParseKeywordEcho ()},
			{"for",		(p) => p.ParseKeywordFor ()},
			{"if",		(p) => p.ParseKeywordIf ()},
			{"return",	(p) => p.ParseKeywordReturn ()},
			{"set",		(p) => p.ParseKeywordSet ()},
			{"while",	(p) => p.ParseKeywordWhile ()}
		};

		#endregion

		#region Constructors

		public	Parser (ISetting setting)
		{
			this.lexer = new Lexer (setting.BlockBegin, setting.BlockContinue, setting.BlockEnd);
			this.trimmer = setting.Trimmer;
		}

		#endregion

		#region Methods / Public

		public INode	Parse (TextReader reader)
		{
			INode	node;

			this.lexer.Reset (reader);
			this.lexer.Next (LexerMode.Raw);

			node = this.ParseRaw ();

			if (this.lexer.Current.Type != LexemType.EndOfFile)
				throw new UnexpectedException (this.lexer, "end of file");

			return node;
		}

		#endregion

		#region Methods / Private

		private INode ParseAssignment (ScopeMode mode)
		{
			List<string>	arguments;
			Func<ScopeMode, INode>	build;
			string					name;

			arguments = new List<string> ();
			name = this.ParseName ();

			switch (this.lexer.Current.Type)
			{
				case LexemType.ParenthesisBegin:
					arguments = new List<string> ();

					for (this.lexer.Next (LexerMode.Block); this.lexer.Current.Type != LexemType.ParenthesisEnd; )
					{
						arguments.Add (this.ParseName ());

						if (this.lexer.Current.Type == LexemType.Comma)
							this.lexer.Next (LexerMode.Block);
					}

					this.lexer.Next (LexerMode.Block);

					build = (m) => new AssignFunctionNode (name, arguments, this.ParseBody (), m); 

					break;

				default:
					build = (m) => new AssignValueNode (name, this.ParseStatement (), m);

					break;
			}

			switch (this.lexer.Current.Type)
			{
				case LexemType.Symbol:
					if (mode == ScopeMode.Closest)
					{
						// <TODO> remove legacy keywords handling
						if (this.lexer.Current.Content == "as")
						{
							this.lexer.Next (LexerMode.Block);

							mode = ScopeMode.Local;
						}
						else
						// </TODO>
							this.ParseExpected (LexemType.Symbol, "to", "'to' keyword");
					}
					else
						this.ParseExpected (LexemType.Symbol, "as", "'as' keyword");

					return build (mode);

				default:
					this.lexer.Next (LexerMode.Raw);

					return new AssignValueNode (name, VoidExpression.Instance, mode);
			}
		}

		private INode ParseBlock ()
		{
			KeywordDelegate keyword;
			INode			node;

			if (this.lexer.Current.Type == LexemType.Symbol && Parser.keywords.TryGetValue (this.lexer.Current.Content, out keyword))
				this.lexer.Next (LexerMode.Block);
			else
				keyword = (p) => p.ParseKeywordEcho ();

			node = keyword (this);

			if (this.lexer.Current.Type != LexemType.BlockEnd)
				throw new UnexpectedException (this.lexer, "end of block");

			this.lexer.Next (LexerMode.Raw);

			return node;
		}

		private INode ParseBody ()
		{
			if (this.lexer.Current.Type != LexemType.Colon)
				throw new UnexpectedException (this.lexer, "body separator (':')");

			this.lexer.Next (LexerMode.Raw);

			return this.ParseRaw ();
		}

		private void ParseExpected (LexemType type, string value, string expected)
		{
			if (this.lexer.Current.Type != type || this.lexer.Current.Content != value)
				throw new UnexpectedException (this.lexer, expected);

			this.lexer.Next (LexerMode.Block);
		}

		private IExpression ParseExpression ()
		{
			List<IExpression>								arguments;
			List<KeyValuePair<IExpression, IExpression>>	elements;
			IExpression										expression;
			int												index;
			IExpression										key;
			decimal											number;
			IExpression										value;

			switch (this.lexer.Current.Type)
			{
				case LexemType.BracketBegin:
					elements = new List<KeyValuePair<IExpression, IExpression>> ();
					index = 0;

					for (this.lexer.Next (LexerMode.Block); this.lexer.Current.Type != LexemType.BracketEnd; )
					{
						key = this.ParseExpression ();

						if (this.lexer.Current.Type == LexemType.Colon)
						{
							this.lexer.Next (LexerMode.Block);

							value = this.ParseExpression ();
						}
						else
						{
							value = key;
							key = new NumberExpression (index++);
						}

						elements.Add (new KeyValuePair<IExpression, IExpression> (key, value));

						if (this.lexer.Current.Type == LexemType.Comma)
							this.lexer.Next (LexerMode.Block);
					}

					this.lexer.Next (LexerMode.Block);

					expression = new ArrayExpression (elements);

					break;

				case LexemType.Number:
					expression = new NumberExpression (decimal.TryParse (this.lexer.Current.Content, NumberStyles.Number, CultureInfo.InvariantCulture, out number) ? number : 0);

					this.lexer.Next (LexerMode.Block);

					break;

				case LexemType.String:
					expression = new StringExpression (this.lexer.Current.Content);

					this.lexer.Next (LexerMode.Block);

					break;

				case LexemType.Symbol:
					expression = new NameExpression (this.ParseName ());

					break;

				default:
					throw new UnexpectedException (this.lexer, "expression");
			}

			while (true)
			{
				switch (this.lexer.Current.Type)
				{
					case LexemType.BracketBegin:
						this.lexer.Next (LexerMode.Block);

						value = this.ParseExpression ();

						if (this.lexer.Current.Type != LexemType.BracketEnd)
							throw new UnexpectedException (this.lexer, "array index end (']')");

						this.lexer.Next (LexerMode.Block);

						expression = new AccessExpression (expression, value);

						break;

					case LexemType.Dot:
						this.lexer.Next (LexerMode.Block);

						if (this.lexer.Current.Type != LexemType.Symbol)
							throw new UnexpectedException (this.lexer, "field name");

						expression = new AccessExpression (expression, new StringExpression (this.lexer.Current.Content));

						this.lexer.Next (LexerMode.Block);

						break;

					case LexemType.ParenthesisBegin:
						arguments = new List<IExpression> ();

						for (this.lexer.Next (LexerMode.Block); this.lexer.Current.Type != LexemType.ParenthesisEnd; )
						{
							arguments.Add (this.ParseExpression ());

							if (this.lexer.Current.Type == LexemType.Comma)
								this.lexer.Next (LexerMode.Block);
						}

						this.lexer.Next (LexerMode.Block);

						expression = new CallExpression (expression, arguments);

						break;

					default:
						return expression;
				}
			}
		}

		private INode ParseKeywordComment ()
		{
			do
			{
				this.lexer.Next (LexerMode.Raw);
			}
			while (this.lexer.Current.Type == LexemType.Text);

			return null;
		}

		private INode ParseKeywordDeclare ()
		{
			return this.ParseAssignment (ScopeMode.Local);
		}

		private INode ParseKeywordDump ()
		{
			return new DumpNode (this.ParseStatement ());
		}

		private INode ParseKeywordEcho ()
		{
			return new EchoNode (this.ParseStatement ());
		}

		private INode ParseKeywordFor ()
		{
			INode			body;
			INode			empty;
			IExpression		from;
			string			key;
			string			value;

			key = this.ParseName ();

			if (this.lexer.Current.Type == LexemType.Comma)
			{
				this.lexer.Next (LexerMode.Block);

				value = this.ParseName ();
			}
			else
			{
				value = key;
				key = null;
			}

			this.ParseExpected (LexemType.Symbol, "in", "'in' keyword");

			from = this.ParseExpression ();
			body = this.ParseBody ();

			if (this.lexer.Current.Type == LexemType.BlockContinue)
			{
				this.lexer.Next (LexerMode.Block);

				this.ParseExpected (LexemType.Symbol, "empty", "'empty' keyword");

				empty = this.ParseBody ();
			}
			else
				empty = null;

			return new ForNode (from, key, value, body, empty);
		}

		private INode ParseKeywordIf ()
		{
			List<IfNode.Branch>	branches;
			INode				fallback;
			IExpression			test;

			branches = new List<IfNode.Branch> ();
			fallback = null;
			test = this.ParseExpression ();

			branches.Add (new IfNode.Branch (test, this.ParseBody ()));

			while (fallback == null && this.lexer.Current.Type == LexemType.BlockContinue)
			{
				this.lexer.Next (LexerMode.Block);

				switch (this.lexer.Current.Type == LexemType.Symbol ? this.lexer.Current.Content : string.Empty)
				{
					case "elif":
						this.lexer.Next (LexerMode.Block);

						test = this.ParseExpression ();

						branches.Add (new IfNode.Branch (test, this.ParseBody ()));

						break;

					case "else":
						this.lexer.Next (LexerMode.Block);

						fallback = this.ParseBody ();

						break;

					default:
						throw new UnexpectedException (this.lexer, "'elif' or 'else' keyword");
				}
			}

			return new IfNode (branches, fallback);
		}

		private INode ParseKeywordReturn ()
		{
			return new ReturnNode (this.ParseStatement ());
		}

		private INode ParseKeywordSet ()
		{
			return this.ParseAssignment (ScopeMode.Closest);
		}

		private INode ParseKeywordWhile ()
		{
			IExpression test = this.ParseExpression ();

			return new WhileNode (test, this.ParseBody ());
		}

		private string ParseName ()
		{
			string	name;

			if (this.lexer.Current.Type != LexemType.Symbol)
				throw new UnexpectedException (this.lexer, "variable name");

			name = this.lexer.Current.Content;

			this.lexer.Next (LexerMode.Block);

			return name;
		}

		private INode ParseRaw ()
		{
			List<INode>	nodes;
			INode		node;
			string		text;

			nodes = new List<INode> ();

			while (true)
			{
				switch (this.lexer.Current.Type)
				{
					case LexemType.BlockBegin:
						this.lexer.Next (LexerMode.Block);

						node = this.ParseBlock ();

						if (node != null)
							nodes.Add (node);

						break;

					case LexemType.BlockContinue:
					case LexemType.BlockEnd:
					case LexemType.EndOfFile:
						return nodes.Count != 1 ? new CompositeNode (nodes) : nodes[0];

					case LexemType.Text:
						text = this.trimmer (this.lexer.Current.Content);

						nodes.Add (new TextNode (text));

						this.lexer.Next (LexerMode.Raw);

						break;

					default:
						throw new UnexpectedException (this.lexer, "text or block begin ('{')");
				}
			}
		}

		private IExpression ParseStatement ()
		{
			IExpression expression;

			expression = this.ParseExpression ();

			this.lexer.Next (LexerMode.Raw);

			return expression;
		}

		#endregion

		#region Types

		private delegate INode	KeywordDelegate (Parser parser);

		#endregion
	}
}
