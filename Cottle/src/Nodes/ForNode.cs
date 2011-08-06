﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Cottle.Expressions;
using Cottle.Values;

namespace   Cottle.Nodes
{
    sealed class    ForNode : INode
    {
        #region Attributes

        private INode           body;

        private INode           empty;

        private IExpression     from;

        private NameExpression   key;

        private NameExpression   value;

        #endregion

        #region Constructors

        public  ForNode (IExpression from, NameExpression key, NameExpression value, INode body, INode empty)
        {
            this.body = body;
            this.empty = empty;
            this.from = from;
            this.key = key;
            this.value = value;
        }

        #endregion

        #region Methods

        public bool Apply (Scope scope, TextWriter output, out Value result)
        {
            ICollection<KeyValuePair<Value, Value>>   collection = this.from.Evaluate (scope, output).Fields;
            bool                                        exit;

            if (collection.Count > 0)
            {
                foreach (KeyValuePair<Value, Value> pair in collection)
                {
                    scope.Enter ();

                    if (this.key != null)
                        this.key.Set (scope, pair.Key, Scope.SetMode.LOCAL);

                    if (this.value != null)
                        this.value.Set (scope, pair.Value, Scope.SetMode.LOCAL);

                    exit = this.body.Apply (scope, output, out result);

                    scope.Leave ();

                    if (exit)
                        return true;
                }
            }
            else if (this.empty != null)
                return this.empty.Apply (scope, output, out result);

            result = UndefinedValue.Instance;

            return false;
        }

        public void Debug (TextWriter output)
        {
            output.Write ("{for ");

            if (this.key != null)
            {
                output.Write (this.key);
                output.Write (", ");
            }

            output.Write (this.value);
            output.Write (" in ");
            output.Write (this.from);
            output.Write (": ");

            this.body.Debug (output);

            if (this.empty != null)
            {
                output.Write ("|empty:");

                this.empty.Debug (output);
            }

            output.Write ('}');
        }

        #endregion
    }
}
