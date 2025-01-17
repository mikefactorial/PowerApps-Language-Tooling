// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.PowerPlatform.Formulas.Tools.EditorState;
using Microsoft.PowerPlatform.Formulas.Tools.IR;
using Microsoft.PowerPlatform.Formulas.Tools.MergeTool.Deltas;
using Microsoft.PowerPlatform.Formulas.Tools.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.PowerPlatform.Formulas.Tools.MergeTool
{
    internal class ControlDiffVisitor : DefaultVisitor<ControlDiffContext>
    {
        private List<IDelta> _deltas;
        private readonly EditorStateStore _editorStateStore;

        public static IEnumerable<IDelta> GetControlDelta(BlockNode ours, BlockNode parent, EditorStateStore stateStore, bool isInComponent)
        {
            var visitor = new ControlDiffVisitor(stateStore);
            visitor.Visit(ours, new ControlDiffContext(new ControlPath(new List<string>()), parent, isInComponent));

            return visitor._deltas;
        }

        private ControlDiffVisitor(EditorStateStore stateStore)
        {
            _deltas = new List<IDelta>();
            _editorStateStore = stateStore;
        }

        private Dictionary<string, ControlState> GetSubtreeStates(BlockNode node)
        {
            return GetSubtreeStatesImpl(node).ToDictionary(state => state.Name);
        }

        private IEnumerable<ControlState> GetSubtreeStatesImpl(BlockNode node)
        {
            var childstates = node.Children?.SelectMany(child => GetSubtreeStatesImpl(child)) ?? Enumerable.Empty<ControlState>();

            if (!_editorStateStore.TryGetControlState(node.Name.Identifier, out var state))
                return childstates;

            return childstates.Concat(new List<ControlState>() { state });
        }

        public override void Visit(BlockNode node, ControlDiffContext context)
        {
            if (!(context.Theirs is BlockNode theirs))
                return;

            if (node.Name.Kind.TypeName != theirs.Name.Kind.TypeName)
            {
                _deltas.Add(new RemoveControl(context.Path, node.Name.Identifier, context.IsInComponent));
                _deltas.Add(new AddControl(context.Path, node, GetSubtreeStates(node), context.IsInComponent));

                return;
            }

            var controlPath = context.Path.Append(node.Name.Identifier);

            var theirChildrenDict = theirs.Children.ToDictionary(child => child.Name.Identifier);
            foreach (var child in node.Children)
            {
                var childName = child.Name.Identifier;
                if (theirChildrenDict.TryGetValue(childName, out var theirChild))
                {
                    child.Accept(this, new ControlDiffContext(controlPath, theirChild, context.IsInComponent));
                    theirChildrenDict.Remove(childName);
                }
                else
                {
                    _deltas.Add(new AddControl(controlPath, child, GetSubtreeStates(child), context.IsInComponent));
                }
            }
            foreach (var kvp in theirChildrenDict)
            {
                _deltas.Add(new RemoveControl(controlPath, kvp.Key, context.IsInComponent));
            }

            // Let's handle properties:
            var theirPropDict = theirs.Properties.ToDictionary(prop => prop.Identifier);
            foreach (var prop in node.Properties)
            {
                var propName = prop.Identifier;
                if (theirPropDict.TryGetValue(propName, out var theirProp))
                {
                    prop.Accept(this, new ControlDiffContext(controlPath, theirProp, context.IsInComponent));
                    theirPropDict.Remove(propName);
                }
                else
                {
                    // Added prop
                    _deltas.Add(new ChangeProperty() { ControlPath = controlPath, Expression = prop.Expression.Expression, PropertyName = prop.Identifier });
                }
            }

            foreach (var kvp in theirPropDict)
            {
                // removed prop
                _deltas.Add(new ChangeProperty() { ControlPath = controlPath, PropertyName = kvp.Key, WasRemoved = true });
            }


        }

        public override void Visit(PropertyNode node, ControlDiffContext context)
        {
            if (!(context.Theirs is PropertyNode theirs))
                return;

            // Maybe add smarter diff here eventually
            if (node.Expression.Expression != theirs.Expression.Expression)
                _deltas.Add(new ChangeProperty() { ControlPath = context.Path, Expression = node.Expression.Expression, PropertyName = node.Identifier });
        }
    }

    internal class ControlDiffContext
    {
        public IRNode Theirs { get; }
        public ControlPath Path { get; }
        public bool IsInComponent { get; }

        public ControlDiffContext(ControlPath path, IRNode theirs, bool isInComponent)
        {
            Theirs = theirs;
            Path = path;
            IsInComponent = isInComponent;
        }
    }
}
