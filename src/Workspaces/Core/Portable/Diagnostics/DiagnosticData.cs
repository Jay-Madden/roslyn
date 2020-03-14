﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal sealed class DiagnosticData : IEquatable<DiagnosticData?>
    {
        public readonly string Id;
        public readonly string Category;
        public readonly string? Message;
        public readonly string? ENUMessageForBingSearch;

        public readonly DiagnosticSeverity Severity;
        public readonly DiagnosticSeverity DefaultSeverity;
        public readonly bool IsEnabledByDefault;
        public readonly int WarningLevel;
        public readonly IReadOnlyList<string> CustomTags;
        public readonly ImmutableDictionary<string, string> Properties;

        public readonly ProjectId? ProjectId;
        public readonly DiagnosticDataLocation? DataLocation;
        public readonly IReadOnlyCollection<DiagnosticDataLocation> AdditionalLocations;

        /// <summary>
        /// Language name (<see cref="LanguageNames"/>) or null if the diagnostic is not associated with source code.
        /// </summary>
        public readonly string? Language;

        public readonly string? Title;
        public readonly string? Description;
        public readonly string? HelpLink;
        public readonly bool IsSuppressed;

        /// <summary>
        /// Properties for a diagnostic generated by an explicit build.
        /// </summary>
        internal static ImmutableDictionary<string, string> PropertiesForBuildDiagnostic { get; }
            = ImmutableDictionary<string, string>.Empty.Add(WellKnownDiagnosticPropertyNames.Origin, WellKnownDiagnosticTags.Build);

        public DiagnosticData(
            string id,
            string category,
            string? message,
            string? enuMessageForBingSearch,
            DiagnosticSeverity severity,
            DiagnosticSeverity defaultSeverity,
            bool isEnabledByDefault,
            int warningLevel,
            IReadOnlyList<string> customTags,
            ImmutableDictionary<string, string> properties,
            ProjectId? projectId,
            DiagnosticDataLocation? location = null,
            IReadOnlyCollection<DiagnosticDataLocation>? additionalLocations = null,
            string? language = null,
            string? title = null,
            string? description = null,
            string? helpLink = null,
            bool isSuppressed = false)
        {
            Id = id;
            Category = category;
            Message = message;
            ENUMessageForBingSearch = enuMessageForBingSearch;

            Severity = severity;
            DefaultSeverity = defaultSeverity;
            IsEnabledByDefault = isEnabledByDefault;
            WarningLevel = warningLevel;
            CustomTags = customTags;
            Properties = properties;

            ProjectId = projectId;
            DataLocation = location;
            AdditionalLocations = additionalLocations ?? Array.Empty<DiagnosticDataLocation>();

            Language = language;
            Title = title;
            Description = description;
            HelpLink = helpLink;
            IsSuppressed = isSuppressed;
        }

        public DocumentId? DocumentId => DataLocation?.DocumentId;
        public bool HasTextSpan => (DataLocation?.SourceSpan).HasValue;

        /// <summary>
        /// Get <see cref="TextSpan"/> if it exists, throws otherwise.
        /// 
        /// Some diagnostic data such as those created from build have original line/column but not <see cref="TextSpan"/>.
        /// In those cases use <see cref="GetTextSpan(DiagnosticDataLocation, SourceText)"/> method instead to calculate span from original line/column.
        /// </summary>
        public TextSpan GetTextSpan()
        {
            Contract.ThrowIfFalse(DataLocation != null && DataLocation.SourceSpan.HasValue);
            return DataLocation.SourceSpan.Value;
        }

        public override bool Equals(object? obj)
            => obj is DiagnosticData data && Equals(data);

        public bool Equals(DiagnosticData? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null)
            {
                return false;
            }

            return
               DataLocation?.OriginalStartLine == other.DataLocation?.OriginalStartLine &&
               DataLocation?.OriginalStartColumn == other.DataLocation?.OriginalStartColumn &&
               Id == other.Id &&
               Category == other.Category &&
               Severity == other.Severity &&
               WarningLevel == other.WarningLevel &&
               IsSuppressed == other.IsSuppressed &&
               ProjectId == other.ProjectId &&
               DocumentId == other.DocumentId &&
               Message == other.Message;
        }

        public override int GetHashCode()
            => Hash.Combine(Id,
               Hash.Combine(Category,
               Hash.Combine(Message,
               Hash.Combine(WarningLevel,
               Hash.Combine(IsSuppressed,
               Hash.Combine(ProjectId,
               Hash.Combine(DocumentId,
               Hash.Combine(DataLocation?.OriginalStartLine ?? 0,
               Hash.Combine(DataLocation?.OriginalStartColumn ?? 0, (int)Severity)))))))));

        public override string ToString()
        {
            return string.Format("{0} {1} {2} {3} {4} {5} ({5}, {6}) [original: {7} ({8}, {9})]",
                Id,
                Severity,
                Message,
                ProjectId,
                DataLocation?.MappedFilePath ?? "",
                DataLocation?.MappedStartLine,
                DataLocation?.MappedStartColumn,
                DataLocation?.OriginalFilePath ?? "",
                DataLocation?.OriginalStartLine,
                DataLocation?.OriginalStartColumn);
        }

        public static TextSpan GetExistingOrCalculatedTextSpan(DiagnosticDataLocation? diagnosticLocation, SourceText text)
        {
            if (diagnosticLocation?.SourceSpan != null)
            {
                return EnsureInBounds(diagnosticLocation.SourceSpan.Value, text);
            }
            else
            {
                return GetTextSpan(diagnosticLocation, text);
            }
        }

        private static TextSpan EnsureInBounds(TextSpan textSpan, SourceText text)
            => TextSpan.FromBounds(
                Math.Min(textSpan.Start, text.Length),
                Math.Min(textSpan.End, text.Length));

        public DiagnosticData WithCalculatedSpan(SourceText text)
        {
            Contract.ThrowIfNull(DocumentId);
            Contract.ThrowIfNull(DataLocation);
            Contract.ThrowIfTrue(HasTextSpan);

            var span = GetTextSpan(DataLocation, text);
            var newLocation = DataLocation.WithCalculatedSpan(span);

            return new DiagnosticData(
                id: Id,
                category: Category,
                message: Message,
                enuMessageForBingSearch: ENUMessageForBingSearch,
                severity: Severity,
                defaultSeverity: DefaultSeverity,
                isEnabledByDefault: IsEnabledByDefault,
                warningLevel: WarningLevel,
                customTags: CustomTags,
                properties: Properties,
                projectId: ProjectId,
                location: newLocation,
                additionalLocations: AdditionalLocations,
                language: Language,
                title: Title,
                description: Description,
                helpLink: HelpLink,
                isSuppressed: IsSuppressed);
        }

        public async Task<Diagnostic> ToDiagnosticAsync(Project project, CancellationToken cancellationToken)
        {
            var location = await DataLocation.ConvertLocationAsync(project, cancellationToken).ConfigureAwait(false);
            var additionalLocations = await AdditionalLocations.ConvertLocationsAsync(project, cancellationToken).ConfigureAwait(false);

            return Diagnostic.Create(
                Id, Category, Message, Severity, DefaultSeverity,
                IsEnabledByDefault, WarningLevel, IsSuppressed, Title, Description, HelpLink,
                location, additionalLocations, customTags: CustomTags, properties: Properties);
        }

        public static (LinePosition startLine, LinePosition endLine) GetLinePositions(DiagnosticDataLocation? dataLocation, SourceText text, bool useMapped)
        {
            var lines = text.Lines;
            if (lines.Count == 0)
            {
                return default;
            }

            var dataLocationStartLine = (useMapped ? dataLocation?.MappedStartLine : dataLocation?.OriginalStartLine) ?? 0;
            var dataLocationStartColumn = (useMapped ? dataLocation?.MappedStartColumn : dataLocation?.OriginalStartColumn) ?? 0;
            var dataLocationEndLine = (useMapped ? dataLocation?.MappedEndLine : dataLocation?.OriginalEndLine) ?? 0;
            var dataLocationEndColumn = (useMapped ? dataLocation?.MappedEndColumn : dataLocation?.OriginalEndColumn) ?? 0;

            if (dataLocationStartLine >= lines.Count)
            {
                var lastLine = lines.GetLinePosition(text.Length);
                return (lastLine, lastLine);
            }

            AdjustBoundaries(dataLocationStartLine, dataLocationStartColumn, dataLocationEndLine, dataLocationEndColumn, lines,
                out var startLine, out var startColumn, out var endLine, out var endColumn);

            var startLinePosition = new LinePosition(startLine, startColumn);
            var endLinePosition = new LinePosition(endLine, endColumn);
            SwapIfNeeded(ref startLinePosition, ref endLinePosition);

            return (startLinePosition, endLinePosition);
        }

        public static TextSpan GetTextSpan(DiagnosticDataLocation? dataLocation, SourceText text)
        {
            (var startLinePosition, var endLinePosition) = GetLinePositions(dataLocation, text, useMapped: false);

            var span = text.Lines.GetTextSpan(new LinePositionSpan(startLinePosition, endLinePosition));
            return EnsureInBounds(TextSpan.FromBounds(Math.Max(span.Start, 0), Math.Max(span.End, 0)), text);
        }

        private static void AdjustBoundaries(int dataLocationStartLine, int dataLocationStartColumn, int dataLocationEndLine, int dataLocationEndColumn,
            TextLineCollection lines, out int startLine, out int startColumn, out int endLine, out int endColumn)
        {
            startLine = dataLocationStartLine;
            var originalStartColumn = dataLocationStartColumn;

            startColumn = Math.Max(originalStartColumn, 0);
            if (startLine < 0)
            {
                startLine = 0;
                startColumn = 0;
            }

            endLine = dataLocationEndLine;
            var originalEndColumn = dataLocationEndColumn;

            endColumn = Math.Max(originalEndColumn, 0);
            if (endLine < 0)
            {
                endLine = startLine;
                endColumn = startColumn;
            }
            else if (endLine >= lines.Count)
            {
                endLine = lines.Count - 1;
                endColumn = lines[endLine].EndIncludingLineBreak;
            }
        }

        private static void SwapIfNeeded(ref LinePosition startLinePosition, ref LinePosition endLinePosition)
        {
            if (endLinePosition < startLinePosition)
            {
                var temp = startLinePosition;
                startLinePosition = endLinePosition;
                endLinePosition = temp;
            }
        }

        private static DiagnosticDataLocation? CreateLocation(Document? document, Location location)
        {
            if (document == null)
            {
                return null;
            }

            GetLocationInfo(document, location, out var sourceSpan, out var originalLineInfo, out var mappedLineInfo);

            var mappedStartLine = mappedLineInfo.StartLinePosition.Line;
            var mappedStartColumn = mappedLineInfo.StartLinePosition.Character;
            var mappedEndLine = mappedLineInfo.EndLinePosition.Line;
            var mappedEndColumn = mappedLineInfo.EndLinePosition.Character;

            var originalStartLine = originalLineInfo.StartLinePosition.Line;
            var originalStartColumn = originalLineInfo.StartLinePosition.Character;
            var originalEndLine = originalLineInfo.EndLinePosition.Line;
            var originalEndColumn = originalLineInfo.EndLinePosition.Character;

            return new DiagnosticDataLocation(document.Id, sourceSpan,
                originalLineInfo.Path, originalStartLine, originalStartColumn, originalEndLine, originalEndColumn,
                mappedLineInfo.GetMappedFilePathIfExist(), mappedStartLine, mappedStartColumn, mappedEndLine, mappedEndColumn);
        }

        public static DiagnosticData Create(Diagnostic diagnostic, OptionSet options)
        {
            Debug.Assert(diagnostic.Location == null || !diagnostic.Location.IsInSource);
            return Create(diagnostic, projectId: null, language: null, options, location: null, additionalLocations: null, additionalProperties: null);
        }

        public static DiagnosticData Create(Diagnostic diagnostic, Project project)
        {
            Debug.Assert(diagnostic.Location == null || !diagnostic.Location.IsInSource);
            return Create(diagnostic, project.Id, project.Language, project.Solution.Options, location: null, additionalLocations: null, additionalProperties: null);
        }

        public static DiagnosticData Create(Diagnostic diagnostic, Document document)
        {
            var project = document.Project;
            var location = CreateLocation(document, diagnostic.Location);

            var additionalLocations = diagnostic.AdditionalLocations.Count == 0
                ? (IReadOnlyCollection<DiagnosticDataLocation>)Array.Empty<DiagnosticDataLocation>()
                : diagnostic.AdditionalLocations.Where(loc => loc.IsInSource)
                                                .Select(loc => CreateLocation(document.Project.GetDocument(loc.SourceTree), loc))
                                                .WhereNotNull()
                                                .ToReadOnlyCollection();

            var additionalProperties = GetAdditionalProperties(document, diagnostic);

            var documentPropertiesService = document.Services.GetService<DocumentPropertiesService>();
            var diagnosticsLspClientName = documentPropertiesService?.DiagnosticsLspClientName;

            if (diagnosticsLspClientName != null)
            {
                if (additionalProperties == null)
                {
                    additionalProperties = ImmutableDictionary.Create<string, string>();
                }

                additionalProperties = additionalProperties.Add(nameof(documentPropertiesService.DiagnosticsLspClientName), diagnosticsLspClientName);
            }

            return Create(diagnostic,
                project.Id,
                project.Language,
                project.Solution.Options,
                location,
                additionalLocations,
                additionalProperties);
        }

        private static DiagnosticData Create(
            Diagnostic diagnostic,
            ProjectId? projectId,
            string? language,
            OptionSet options,
            DiagnosticDataLocation? location,
            IReadOnlyCollection<DiagnosticDataLocation>? additionalLocations,
            ImmutableDictionary<string, string>? additionalProperties)
        {
            return new DiagnosticData(
                diagnostic.Id,
                diagnostic.Descriptor.Category,
                diagnostic.GetMessage(CultureInfo.CurrentUICulture),
                diagnostic.GetBingHelpMessage(options),
                diagnostic.Severity,
                diagnostic.DefaultSeverity,
                diagnostic.Descriptor.IsEnabledByDefault,
                diagnostic.WarningLevel,
                diagnostic.Descriptor.CustomTags.AsImmutableOrEmpty(),
                (additionalProperties == null) ? diagnostic.Properties : diagnostic.Properties.AddRange(additionalProperties),
                projectId,
                location,
                additionalLocations,
                language: language,
                title: diagnostic.Descriptor.Title.ToString(CultureInfo.CurrentUICulture),
                description: diagnostic.Descriptor.Description.ToString(CultureInfo.CurrentUICulture),
                helpLink: diagnostic.Descriptor.HelpLinkUri,
                isSuppressed: diagnostic.IsSuppressed);
        }

        private static ImmutableDictionary<string, string>? GetAdditionalProperties(Document document, Diagnostic diagnostic)
        {
            var service = document.GetLanguageService<IDiagnosticPropertiesService>();
            return service?.GetAdditionalProperties(diagnostic);
        }

        /// <summary>
        /// Create a host/VS specific diagnostic with the given descriptor and message arguments for the given project.
        /// Note that diagnostic created through this API cannot be suppressed with in-source suppression due to performance reasons (see the PERF remark below for details).
        /// </summary>
        public static bool TryCreate(DiagnosticDescriptor descriptor, string[] messageArguments, Project project, [NotNullWhen(true)]out DiagnosticData? diagnosticData)
        {
            diagnosticData = null;

            DiagnosticSeverity effectiveSeverity;
            if (project.SupportsCompilation)
            {
                // Get the effective severity of the diagnostic from the compilation options.
                // PERF: We do not check if the diagnostic was suppressed by a source suppression, as this requires us to force complete the assembly attributes, which is very expensive.
                var reportDiagnostic = descriptor.GetEffectiveSeverity(project.CompilationOptions!);
                if (reportDiagnostic == ReportDiagnostic.Suppress)
                {
                    // Rule is disabled by compilation options.
                    return false;
                }

                effectiveSeverity = GetEffectiveSeverity(reportDiagnostic, descriptor.DefaultSeverity);
            }
            else
            {
                effectiveSeverity = descriptor.DefaultSeverity;
            }

            var diagnostic = Diagnostic.Create(descriptor, Location.None, effectiveSeverity, additionalLocations: null, properties: null, messageArgs: messageArguments);
            diagnosticData = Create(diagnostic, project);
            return true;
        }

        private static DiagnosticSeverity GetEffectiveSeverity(ReportDiagnostic effectiveReportDiagnostic, DiagnosticSeverity defaultSeverity)
        {
            switch (effectiveReportDiagnostic)
            {
                case ReportDiagnostic.Default:
                    return defaultSeverity;

                case ReportDiagnostic.Error:
                    return DiagnosticSeverity.Error;

                case ReportDiagnostic.Hidden:
                    return DiagnosticSeverity.Hidden;

                case ReportDiagnostic.Info:
                    return DiagnosticSeverity.Info;

                case ReportDiagnostic.Warn:
                    return DiagnosticSeverity.Warning;

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        private static void GetLocationInfo(Document document, Location location, out TextSpan sourceSpan, out FileLinePositionSpan originalLineInfo, out FileLinePositionSpan mappedLineInfo)
        {
            var diagnosticSpanMappingService = document.Project.Solution.Workspace.Services.GetService<IWorkspaceVenusSpanMappingService>();
            if (diagnosticSpanMappingService != null)
            {
                diagnosticSpanMappingService.GetAdjustedDiagnosticSpan(document.Id, location, out sourceSpan, out originalLineInfo, out mappedLineInfo);
                return;
            }

            sourceSpan = location.SourceSpan;
            originalLineInfo = location.GetLineSpan();
            mappedLineInfo = location.GetMappedLineSpan();
        }

        /// <summary>
        /// Returns true if the diagnostic was generated by an explicit build, not live analysis.
        /// </summary>
        /// <returns></returns>
        internal bool IsBuildDiagnostic()
        {
            return Properties.TryGetValue(WellKnownDiagnosticPropertyNames.Origin, out var value) &&
                value == WellKnownDiagnosticTags.Build;
        }
    }
}
