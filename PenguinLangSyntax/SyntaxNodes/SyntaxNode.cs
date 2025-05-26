global using PenguinLangSyntax;
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using Antlr4.Runtime;
global using Antlr4.Runtime.Misc;
global using static PenguinLangSyntax.PenguinLangParser;
global using PenguinLangSyntax.SyntaxNodes;

namespace PenguinLangSyntax.SyntaxNodes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class ChildrenNodeAttribute : Attribute { }

    public abstract class SyntaxNode : ISyntaxNode
    {
        public uint ScopeDepth { get; set; }

        public abstract string BuildText();

        public abstract void FromString(string text, uint scopeDepth, ErrorReporter reporter);

        public SourceLocation SourceLocation { get; set; } = SourceLocation.Empty();

        public string Text => BuildText();

        public string SourceText { get; set; } = string.Empty;

        protected string shorten(string s)
        {
            var str = s.Trim().Replace("\n", " ").Replace("\r", "");
            return str.Length > 30 ? string.Concat(str.AsSpan(0, 27), "...") : str;
        }

        public override string ToString() => $"[{GetType().Name}] {shorten((this as ISyntaxNode).BuildText() ?? string.Empty)}";

        public virtual void Build(SyntaxWalker walker, ParserRuleContext context)
        {
            SourceText = context.Start.InputStream.GetText(new Interval(context.Start.StartIndex, context.Stop.StopIndex));
            var fileNameIdentifier = $"{Path.GetFileNameWithoutExtension(walker.FileName)}_{((uint)walker.FileName.GetHashCode()) % 0xFFFF}";
            SourceLocation = new SourceLocation(walker.FileName, fileNameIdentifier, context.Start.Line, context.Stop.Line, context.Start.Column, context.Stop.Column);
            ScopeDepth = walker.CurrentScopeDepth;
        }

        public void TraverseChildren(Func<SyntaxNode, SyntaxNode, bool> callback)
        {
            try
            {
                (this as ISyntaxNode).TraverseChildrenImpl(callback, this);
            }
            catch (EndOfStreamException)
            {
                // ok
            }
        }

        public IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return new List<string> { $"{new string(' ', indentLevel * 2)} {note}: {ToString()}" }
                .Concat((this as ISyntaxNode).Children.SelectMany(item => item.Value.PrettyPrint(indentLevel + 1, item.Key)));
        }

        public T Build<T>(Action<T> init) where T : ISyntaxNode, new()
        {
            var result = new T
            {
                SourceLocation = this.SourceLocation,
                ScopeDepth = this.ScopeDepth,
                SourceText = this.SourceText
            };
            init(result);
            return result;
        }

        public static T Build<T>(SyntaxWalker walker, ParserRuleContext context) where T : ISyntaxNode, new()
        {
            var result = new T();
            result.Build(walker, context);
            return result;
        }

    }

    public interface ISyntaxNode : IPrettyPrint
    {
        public uint ScopeDepth { get; set; }

        public abstract void FromString(string text, uint scopeDepth, ErrorReporter reporter);

        public SourceLocation SourceLocation { get; set; }

        public string Text { get; }

        public string SourceText { get; set; }

        public string BuildText();

        public void Build(SyntaxWalker walker, ParserRuleContext context);

        public List<KeyValuePair<string, ISyntaxNode>> Children
        {
            get
            {
                var result = new List<KeyValuePair<string, ISyntaxNode>>();
                var properties = this.GetType().GetProperties()
                    .Where(p => p.GetCustomAttributes(typeof(ChildrenNodeAttribute), true).Length != 0);

                foreach (var property in properties)
                {
                    var value = property.GetValue(this);
                    if (value is SyntaxNode node)
                    {
                        result.Add(new KeyValuePair<string, ISyntaxNode>(property.Name, node));
                    }
                    else if (value is IEnumerable<SyntaxNode> nodes)
                    {
                        result.AddRange(nodes.Select(n => new KeyValuePair<string, ISyntaxNode>(property.Name, n)));
                    }
                    else if (value is IEnumerable<ISyntaxNode> nodes2)
                    {
                        result.AddRange(nodes2.Select(n => new KeyValuePair<string, ISyntaxNode>(property.Name, n)));
                    }
                }

                return result;
            }
        }

        public virtual T? FindChild<T>() where T : SyntaxNode
        {
            T? result = null;
            TraverseChildren((n, p) =>
            {
                if (n is T t)
                {
                    result = t;
                    return false;
                }
                return true;
            });
            return result;
        }

        // bool callback(SyntaxNode current, SyntaxNode parent)
        public void TraverseChildrenImpl(Func<SyntaxNode, SyntaxNode, bool> callback, SyntaxNode parent)
        {
            if (!callback((SyntaxNode)this, parent)) throw new EndOfStreamException();

            Children.ForEach(i => i.Value.TraverseChildrenImpl(callback, (SyntaxNode)this));
        }

        public void TraverseChildren(Func<SyntaxNode, SyntaxNode, bool> callback);

        public void ReplaceChild(SyntaxNode oldChild, SyntaxNode? newChild)
        {
            var properties = this.GetType().GetProperties()
                    .Where(p => p.GetCustomAttributes(typeof(ChildrenNodeAttribute), true).Length != 0);

            foreach (var property in properties)
            {
                var value = property.GetValue(this);
                if (value is SyntaxNode node && node == oldChild)
                {
                    property.SetValue(this, newChild);
                }
                else if (value is List<SyntaxNode> nodes)
                {
                    if (nodes.Remove(oldChild))
                    {
                        if (newChild != null)
                            nodes.Add(newChild);
                        break;
                    }
                }
            }
        }
    }
}