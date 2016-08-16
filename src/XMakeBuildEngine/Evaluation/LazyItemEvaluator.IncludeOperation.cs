﻿using Microsoft.Build.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Construction;
using Microsoft.Build.Execution;
using System.Collections.Immutable;
using Microsoft.Build.Shared;
using Microsoft.Build.Internal;

namespace Microsoft.Build.Evaluation
{
    internal partial class LazyItemEvaluator<P, I, M, D>
    {
        class IncludeOperation : LazyItemOperation
        {
            readonly int _elementOrder;
            
            readonly string _rootDirectory;
            
            readonly bool _conditionResult;

            readonly ImmutableList<string> _excludes;

            readonly ImmutableList<ProjectMetadataElement> _metadata;

            public IncludeOperation(IncludeOperationBuilder builder, LazyItemEvaluator<P, I, M, D> lazyEvaluator)
                : base(builder, lazyEvaluator)
            {
                _elementOrder = builder.ElementOrder;
                _rootDirectory = builder.RootDirectory;
                
                _conditionResult = builder.ConditionResult;

                _excludes = builder.Excludes.ToImmutable();
                _metadata = builder.Metadata.ToImmutable();
            }

            protected override ICollection<I> SelectItems(ImmutableList<ItemData>.Builder listBuilder, ImmutableHashSet<string> globsToIgnore)
            {
                List<I> itemsToAdd = new List<I>();

                Lazy<Func<string, bool>> excludeTester = null;
                ImmutableList<string>.Builder excludePatterns = ImmutableList.CreateBuilder<string>();
                if (_excludes != null)
                {
                    // STEP 4: Evaluate, split, expand and subtract any Exclude
                    foreach (string exclude in _excludes)
                    {
                        string excludeExpanded = _expander.ExpandIntoStringLeaveEscaped(exclude, ExpanderOptions.ExpandPropertiesAndItems, _itemElement.ExcludeLocation);
                        IList<string> excludeSplits = ExpressionShredder.SplitSemiColonSeparatedList(excludeExpanded);
                        excludePatterns.AddRange(excludeSplits);
                    }

                    if (excludePatterns.Any())
                    {
                        excludeTester = new Lazy<Func<string, bool>>(() => EngineFileUtilities.GetMatchTester(excludePatterns));
                    }
                }

                foreach (var operation in _operations)
                {
                    if (operation.Item1 == ItemOperationType.Expression)
                    {
                        // STEP 3: If expression is "@(x)" copy specified list with its metadata, otherwise just treat as string
                        bool throwaway;
                        var itemsFromExpression = _expander.ExpandExpressionCaptureIntoItems(
                            (ExpressionShredder.ItemExpressionCapture) operation.Item2, _evaluatorData, _itemFactory, ExpanderOptions.ExpandItems,
                            false /* do not include null expansion results */, out throwaway, _itemElement.IncludeLocation);

                        if (excludeTester != null)
                        {
                            itemsToAdd.AddRange(itemsFromExpression.Where(item => !excludeTester.Value(item.EvaluatedInclude)));
                        }
                        else
                        {
                            itemsToAdd.AddRange(itemsFromExpression);
                        }
                    }
                    else if (operation.Item1 == ItemOperationType.Value)
                    {
                        string value = (string)operation.Item2;

                        if (excludeTester == null ||
                            !excludeTester.Value(value))
                        {
                            var item = _itemFactory.CreateItem(value, value, _itemElement.ContainingProject.FullPath);
                            itemsToAdd.Add(item);
                        }
                    }
                    else if (operation.Item1 == ItemOperationType.Glob)
                    {
                        string glob = (string)operation.Item2;
                        string[] includeSplitFilesEscaped = EngineFileUtilities.GetFileListEscaped(_rootDirectory, glob,
                            excludePatterns.Count > 0 ? (IEnumerable<string>) excludePatterns.Concat(globsToIgnore) : globsToIgnore);
                        foreach (string includeSplitFileEscaped in includeSplitFilesEscaped)
                        {
                            itemsToAdd.Add(_itemFactory.CreateItem(includeSplitFileEscaped, glob, _itemElement.ContainingProject.FullPath));
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(operation.Item1.ToString());
                    }
                }

                return itemsToAdd;
            }

            protected override void MutateItems(ICollection<I> items)
            {
                DecorateItemsWithMetadata(items, _metadata);
            }

            protected override void SaveItems(ICollection<I> items, ImmutableList<ItemData>.Builder listBuilder)
            {
                listBuilder.AddRange(items.Select(item => new ItemData(item, _elementOrder, _conditionResult)));
            }
        }

        class IncludeOperationBuilder : OperationBuilderWithMetadata
        {
            public int ElementOrder { get; set; }
            public string RootDirectory { get; set; }
            
            public bool ConditionResult { get; set; }
            
            public ImmutableList<string>.Builder Excludes { get; set; } = ImmutableList.CreateBuilder<string>();

            public IncludeOperationBuilder(ProjectItemElement itemElement) : base(itemElement)
            {
            }
        }
    }
}
