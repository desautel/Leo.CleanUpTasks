﻿namespace Leo.CleanUpTasks
{
    using Models;
    using Sdl.Core.Globalization;
    using Sdl.Core.Settings;
    using Sdl.FileTypeSupport.Framework.BilingualApi;
    using Sdl.ProjectAutomation.Core;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using Utilities;

    public class SegmentContentHandler : AbstractBilingualContentHandler
    {
        private readonly IProject project = null;
        private readonly IXmlReportGenerator reportGenerator = null;
        private readonly ICleanUpSourceSettings settings = null;
        private IList<ISegmentHandler> handlers = null;

        public SegmentContentHandler(ICleanUpSourceSettings settings, IProject project, IXmlReportGenerator reportGenerator)
        {
            Contract.Requires<ArgumentNullException>(settings != null);
            Contract.Requires<ArgumentNullException>(project != null);
            Contract.Requires<ArgumentNullException>(reportGenerator != null);

            this.settings = settings;
            this.project = project;
            this.reportGenerator = reportGenerator;
        }

        public override void Complete()
        {
            foreach (var handler in handlers)
            {
                if (handler is ConversionCleanupHandler)
                {
                    var placeholderList = ((ConversionCleanupHandler)handler).PlaceholderList;

                    var allPlaceholders = new List<Placeholder>(settings.Placeholders.Count +
                                                                placeholderList.Count);
                    allPlaceholders.AddRange(settings.Placeholders);
                    allPlaceholders.AddRange(placeholderList);

                    settings.Placeholders = allPlaceholders.Distinct().ToList();
                    project.UpdateSettings(((SettingsGroup)settings).SettingsBundle);
                }
            }
        }

        public override void Initialize(IDocumentProperties documentInfo)
        {
            var messageReporter = new CleanUpMessageReporter(MessageReporter);
            handlers = GetHandlers(messageReporter);
        }

        public override void ProcessParagraphUnit(IParagraphUnit paragraphUnit)
        {
            // During project creation, SegmentPairs is only available
            // after Pre-Translate files is called
            // Before that Segment Pairs is empty and only IParagraph units exist
            // TODO: Consider adding a processor for IParagraph units before segmentation

            if (paragraphUnit.IsStructure) { return; }

            foreach (var segPair in paragraphUnit.SegmentPairs)
            {
                var source = segPair.Source;
                foreach (var handler in handlers)
                {
                    source.AcceptVisitor(handler);
                }
            }
        }

        public override void SetFileProperties(IFileProperties fileInfo)
        {
            Contract.Requires<ArgumentNullException>(fileInfo != null);

            CultureInfo cultureInfo = null;

            try
            {
                var sniffInfo = fileInfo.FileConversionProperties?.FileSnifferInfo;
                cultureInfo = sniffInfo?.DetectedSourceLanguage?.First?.CultureInfo;
            }
            catch (UnsupportedLanguageException)
            {
                // We just ignore these and fall back on oridinal comparison
            }
            finally
            {
                settings.SourceCulture = cultureInfo;
            }
        }

        /// <summary>
        /// Gets the segment handlers.
        /// Can add more here when adding additional handlers
        /// </summary>
        /// <returns></returns>
        private IList<ISegmentHandler> GetHandlers(ICleanUpMessageReporter reporter)
        {
            var handlers = new List<ISegmentHandler>();

            if (settings.UseSegmentLocker)
            {
                handlers.Add(new LockHandler(settings, ItemFactory, reporter, reportGenerator));
            }

            if (settings.UseTagCleaner)
            {
                handlers.Add(new TagHandler(settings, new FormattingVisitor(settings), ItemFactory, reporter, reportGenerator));
            }

            if (settings.UseConversionSettings)
            {
                handlers.Add(new ConversionCleanupHandler(settings, LoadConversionFiles(), ItemFactory, reporter, reportGenerator));
            }

            return handlers;
        }

        /// <summary>
        /// Deserializes each conversion file for use in <see cref="ConversionCleanupHandler"/>
        /// </summary>
        /// <returns>A list of <see cref="ConversionItemList"/></returns>
        private List<ConversionItemList> LoadConversionFiles()
        {
            var items = new List<ConversionItemList>(settings.ConversionFiles.Count);

            try
            {
                foreach (var pair in settings.ConversionFiles)
                {
                    if (File.Exists(pair.Key) && pair.Value)
                    {
                        var conversionItemList = XmlUtilities.Deserialize(pair.Key);
                        items.Add(conversionItemList);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // TODO: Log
            }

            return items;
        }
    }
}