﻿namespace Leo.CleanUpTasks
{
    using Microsoft.VisualBasic;
    using Models;
    using Sdl.FileTypeSupport.Framework.BilingualApi;
    using Sdl.FileTypeSupport.Framework.NativeApi;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;
    using Utilities;

    public class ConversionCleanupHandler : SegmentHandlerBase, ISegmentHandler
    {
        private readonly List<ConversionItemList> conversionItemLists = null;
        private readonly List<Placeholder> placeholderList = new List<Placeholder>();
        private readonly StringBuilder plainText = new StringBuilder();
        private readonly IXmlReportGenerator reportGenerator = null;
        private readonly ISettings settings = null;
        private readonly List<ITagPair> tagPairList = new List<ITagPair>();
        private List<Tuple<string, IText>> textList = new List<Tuple<string, IText>>();

        public ConversionCleanupHandler(ISettings settings,
                                    List<ConversionItemList> conversionItems,
                                    IDocumentItemFactory itemFactory,
                                    ICleanUpMessageReporter reporter,
                                    IXmlReportGenerator reportGenerator)
            : base(itemFactory, reporter)
        {
            Contract.Requires<ArgumentNullException>(settings != null);
            Contract.Requires<ArgumentNullException>(conversionItems != null);
            Contract.Requires<ArgumentNullException>(reportGenerator != null);

            this.settings = settings;
            conversionItemLists = conversionItems;
            this.reportGenerator = reportGenerator;
        }

        public List<Placeholder> PlaceholderList { get { return placeholderList; } }

        public void VisitSegment(ISegment segment)
        {
            if (ShouldSkip(segment)) { return; }

            VisitChildren(segment);

            CleanUpSource(segment);

            ResetFields();
        }

        public void VisitTagPair(ITagPair tagPair)
        {
            tagPairList.Add(tagPair);
            VisitChildren(tagPair);
        }

        public void VisitText(IText text)
        {
            var txt = text.Properties.Text;

            textList.Add(new Tuple<string, IText>(txt, text));
        }

        private static VbStrConv ConvertToEnum(SearchText search)
        {
            VbStrConv vbStrConv = VbStrConv.None;
            foreach (var v in search.VbStrConv)
            {
                vbStrConv += (int)v;
            }

            return vbStrConv;
        }

        private bool AttributesChanged(string updatedText, string fullText)
        {
            var tagPair1 = XElement.Parse(updatedText);
            var tagPair2 = XElement.Parse(fullText);

            bool result = false;

            if (tagPair1.HasAttributes && tagPair2.HasAttributes)
            {
                var attrib1 = tagPair1.Attributes();
                var attrib2 = tagPair2.Attributes();

                if (attrib1.Count() == attrib2.Count())
                {
                    foreach (var attrib in attrib1)
                    {
                        result = !attrib2.Any(a => a.Name == attrib.Name && a.Value == attrib.Value);
                    }
                }
            }

            return result;
        }

        private void CleanUpSource(ISegment segment)
        {
            if (textList.Count == 0 && tagPairList.Count == 0)
            {
                return;
            }

            foreach (var itemList in conversionItemLists)
            {
                foreach (var item in itemList.Items)
                {
                    ProcessText(item, segment);
                }
            }
        }

        private bool ContainsTags(string updatedText)
        {
            return Regex.IsMatch(updatedText, "<.+?>");
        }

        private bool ContentChanged(string updatedText, string fullText)
        {
            var match1 = Regex.Match(updatedText, "(<.+?>)(.+?)(</.+?>)");
            var match2 = Regex.Match(fullText, "(<.+?>)(.+?)(</.+?>)");

            if (match1.Success && match2.Success)
            {
                return match1.Groups[2].Value != match2.Groups[2].Value;
            }

            return false;
        }

        private List<IAbstractMarkupData> CreateMarkupData(string input)
        {
            var element = XElement.Parse(input);
            var markupList = new List<IAbstractMarkupData>();

            int skip = 0;

            foreach (var node in element.DescendantNodes())
            {
                if (skip != 0)
                {
                    skip -= 1;
                    continue;
                }

                if (node.NodeType == System.Xml.XmlNodeType.Text)
                {
                    var txt = ((XText)node).Value;
                    markupList.Add(CreateIText(txt));
                }
                else if (node.NodeType == System.Xml.XmlNodeType.Element)
                {
                    var elem = (XElement)node;

                    if (elem.IsEmpty)
                    {
                        var ph = elem.ToString();
                        StorePlaceholder(ph);

                        if (elem.HasAttributes)
                        {
                            var phTag = CreatePlaceHolderTag(ph);
                            markupList.Add(phTag);
                        }
                        else
                        {
                            var phTag = CreatePlaceHolderTag(ph);
                            markupList.Add(phTag);
                        }
                    }
                    else
                    {
                        var rawText = elem.ToString();
                        var match = Regex.Match(rawText, "(<.+?>)(.+?)(</.+?>)");

                        if (match.Success)
                        {
                            var startTag = CreateStartTag(match.Groups[1].Value);
                            var endTag = CreateEndTag(match.Groups[3].Value);
                            var tagPair = CreateTagPair(startTag, endTag);

                            var text = match.Groups[2].Value;

                            if (!ContainsTags(text))
                            {
                                var itext = CreateIText(match.Groups[2].Value);
                                tagPair.Add(itext);

                                // Experimental:
                                // Creation of new formatting
                                var xml = XElement.Parse(rawText);
                                if (xml.Name == "cf" && xml.HasAttributes)
                                {
                                    var formattingGroup = CreateFormattingGroup();
                                    
                                    foreach (var attrib in xml.Attributes())
                                    {
                                        formattingGroup.Add(CreateFormattingItem(attrib.Name.LocalName, attrib.Value));
                                    }

                                    tagPair.StartTagProperties.Formatting = formattingGroup;
                                }

                                markupList.Add(tagPair);
                            }
                            else
                            {
                                Reporter.Report(this, ErrorLevel.Warning, "Nested tags not fully supported", input);
                                break;
                            }

                            skip = 2;
                        }
                    }
                }
            }

            return markupList;
        }

        private void CreatePlaceHolder(string updatedText, IText itext)
        {
            Contract.Requires<ArgumentNullException>(!string.IsNullOrEmpty(updatedText));
            Contract.Requires<ArgumentNullException>(itext != null);

            var match = Regex.Match(updatedText, @"<([^<]+?)\b\s*[^<]*?/>");

            var parent = itext.Parent;

            if (match.Success)
            {
                plainText.Clear();

                var index = itext.IndexInParent;

                foreach (var item in Regex.Split(updatedText, $"({match.Value})"))
                {
                    if (string.IsNullOrEmpty(item))
                    {
                        continue;
                    }

                    if (Regex.IsMatch(item, @"<([^<]+?)\b\s*[^<]*?/>"))
                    {
                        var phTag = CreatePlaceHolderTag(match);

                        StorePlaceholder(match.Value);

                        if (phTag != null)
                        {
                            if (parent.Count > index)
                            {
                                parent.Insert(index++, phTag);
                            }
                            else
                            {
                                parent.Add(phTag);
                            }
                        }
                    }
                    else
                    {
                        if (parent.Count > index)
                        {
                            if (index > 0)
                            {
                                parent.Insert(index++, CreateIText(item));
                            }
                        }
                        else
                        {
                            parent.Add(CreateIText(item));
                        }
                    }
                }

                itext.RemoveFromParent();
            }
            else
            {
                var matchTagPair = Regex.Match(updatedText, "(<.+?>)(.+?)(</.+?>)");

                if (matchTagPair.Success)
                {
                    plainText.Clear();

                    var input = string.Concat("<root>", updatedText, "</root>");

                    var markupData = CreateMarkupData(input);

                    var index = itext.IndexInParent;

                    foreach (var item in markupData)
                    {
                        if (parent.Count > index)
                        {
                            if (index > 0)
                            {
                                parent.Insert(index++, item);
                            }
                        }
                        else
                        {
                            parent.Add(item);
                        }
                    }

                    itext.RemoveFromParent();
                }
                else
                {
                    Reporter.Report(this, ErrorLevel.Warning, "Placeholder not found", $"{updatedText}");
                }
            }
        }

        private void GetFullText(ITagPair tagPair, StringBuilder stringBuilder)
        {
            var startTag = tagPair.StartTagProperties.TagContent;
            stringBuilder.Append(startTag);

            foreach (var item in tagPair.AllSubItems)
            {
                if (item is IText)
                {
                    var txt = ((IText)item).Properties.Text;
                    stringBuilder.Append(txt);
                }
                else if (item is IPlaceholderTag)
                {
                    var tag = ((IPlaceholderTag)item).TagProperties.TagContent;
                    stringBuilder.Append(tag);
                }
                else if (item is ITagPair)
                {
                    GetFullText((ITagPair)item, stringBuilder);
                }
            }

            var endTag = tagPair.EndTagProperties.TagContent;
            stringBuilder.Append(endTag);
        }

        private void ProcessSingleString(string original, IText itext, ConversionItem item)
        {
            var search = item.Search;
            var replacement = item.Replacement;

            if (replacement.Placeholder)
            {
                string updatedText;
                bool result = false;

                if (search.UseRegex)
                {
                    result = TryRegexUpdate(original, item, out updatedText);
                }
                else if (search.WholeWord)
                {
                    result = TryRegexUpdate(original, item, out updatedText, true);
                }
                else
                {
                    result = TryNormalStringUpdate(original, item, out updatedText);
                }

                if (result)
                {
                    if (itext.Parent is ISegment)
                    {
                        var segment = (ISegment)itext.Parent;
                        reportGenerator.AddConversionItem(segment.Properties.Id.Id, original, updatedText, item.Search.Text, item.Replacement.Text);
                    }

                    CreatePlaceHolder(updatedText, itext);
                }
            }
            else
            {
                string updatedText;
                var result = TryReplaceString(original, item, search, out updatedText);

                if (result)
                {
                    if (itext.Parent is ISegment)
                    {
                        var segment = (ISegment)itext.Parent;
                        reportGenerator.AddConversionItem(segment.Properties.Id.Id, original, updatedText, item.Search.Text, item.Replacement.Text);
                    }

                    UpdateIText(updatedText, itext);

                    plainText.Replace(original, updatedText);
                }
            }
        }

        private void ProcessText(ConversionItem item, ISegment segment)
        {
            Contract.Requires<ArgumentNullException>(item != null);

            if (item.Search.TagPair)
            {
                TagPairUpdate(item);
                return;
            }

            var list = new List<Tuple<string, IText>>();

            foreach (var pair in textList)
            {
                if (string.IsNullOrEmpty(pair.Item1))
                {
                    Reporter.Report(this, ErrorLevel.Warning, "Source was empty", new TextLocation(pair.Item2, 0), new TextLocation(pair.Item2, pair.Item1.Length));
                }
                else
                {
                    plainText.Append(pair.Item1);

                    ProcessSingleString(pair.Item1, pair.Item2, item);

                    var text = plainText.ToString();

                    if (!string.IsNullOrEmpty(text) && pair.Item2.Parent != null)
                    {
                        list.Add(new Tuple<string, IText>(plainText.ToString(), pair.Item2));
                    }

                    plainText.Clear();
                }
            }

            // Replace list with current version
            textList = list;
        }

        private void ReplaceTagPair(string updatedText, ITagPair tagPair, IAbstractMarkupDataContainer parent)
        {
            var input = string.Concat("<root>", updatedText, "</root>");

            var markupData = CreateMarkupData(input);

            var index = tagPair.IndexInParent;

            foreach (var item in markupData)
            {
                if (parent.Count > index)
                {
                    if (index > 0)
                    {
                        parent.Insert(index++, item);
                    }
                }
                else
                {
                    parent.Add(item);
                }
            }

            tagPair.RemoveFromParent();
        }

        private void ResetFields()
        {
            textList.Clear();
            tagPairList.Clear();
        }

        private bool SearchTextValid(SearchText search)
        {
            if (string.IsNullOrWhiteSpace(search.Text))
            {
                Reporter.Report(this, ErrorLevel.Warning, "Search text is empty", $"{search.Text}");
                return false;
            }
            else
            {
                return true;
            }
        }

        private void StorePlaceholder(string value, bool isTagPair = false)
        {
            if (!placeholderList.Any(p => p.Content.Contains(value)))
            {
                placeholderList.Add(new Placeholder() { Content = value, IsTagPair = isTagPair });
            }
        }

        private void TagPairUpdate(ConversionItem item)
        {
            var search = item.Search;
            var replacement = item.Replacement;

            if (SearchTextValid(search))
            {
                foreach (var tagpair in tagPairList)
                {
                    var stringBuilder = new StringBuilder();
                    GetFullText(tagpair, stringBuilder);
                    var fullText = stringBuilder.ToString();
                    string updatedText;
                    var result = TryRegexUpdateTagPair(fullText, item, out updatedText);

                    if (result)
                    {
                        UpdateTagPair(updatedText, fullText, tagpair);
                    }
                }
            }
        }

        private bool TagsChanged(string updatedText, string fullText)
        {
            var match1 = Regex.Match(updatedText, "(<.+?>)(.+?)(</.+?>)");
            var match2 = Regex.Match(fullText, "(<.+?>)(.+?)(</.+?>)");

            if (match1.Success && match2.Success)
            {
                var startTag1 = XElement.Parse(updatedText).Name;
                var startTag2 = XElement.Parse(fullText).Name;

                return startTag1 != startTag2;
            }

            return true;
        }

        private bool TryNormalStringUpdate(string original, ConversionItem item, out string updatedText)
        {
            var search = item.Search;
            var replacement = item.Replacement;

            updatedText = null;

            if (SearchTextValid(search))
            {
                if (original.NormalStringCompare(search.Text, search.CaseSensitive, settings.SourceCulture))
                {
                    updatedText = original.NormalStringReplace(search.Text, replacement.Text, search.CaseSensitive);
                }
            }

            return updatedText != null;
        }

        private bool TryRegexUpdate(string original, ConversionItem item, out string updatedText, bool wholeWord = false)
        {
            var search = item.Search;
            var replacement = item.Replacement;

            string searchText;
            if (wholeWord)
            {
                searchText = "\\b" + Regex.Escape(search.Text) + "\\b";
            }
            else
            {
                searchText = search.Text;
            }

            updatedText = null;

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                if (original.RegexCompare(searchText, search.CaseSensitive))
                {
                    updatedText = original.RegexReplace(searchText, replacement, search.CaseSensitive);
                }
            }

            // Because the method returns input unchanged if there is no match, you can use
            // the Object.ReferenceEquals method to determine whether the method has made any
            // replacements to the input string.
            return (updatedText != null) && !object.ReferenceEquals(original, updatedText);
        }

        private bool TryRegexUpdateTagPair(string original, ConversionItem item, out string updatedText, bool wholeWord = false)
        {
            var result = false;
            var search = item.Search;
            var replacement = item.Replacement;
            updatedText = null;

            if (search.StrConv == false &&
                replacement.ToLower == false &&
                replacement.ToUpper == false)
            {
                return TryRegexUpdate(original, item, out updatedText, wholeWord);
            }

            // Create a dummy ConversionItem has ToUpper, ToLower and StrConv set to false
            var dummy = new ConversionItem()
            {
                Search = new SearchText()
                {
                    CaseSensitive = search.CaseSensitive,
                    Text = search.Text,
                    UseRegex = search.UseRegex,
                    WholeWord = search.WholeWord
                },
                Replacement = new ReplacementText()
                {
                    Placeholder = replacement.Placeholder,
                    Text = replacement.Text
                }
            };

            string updatedTextTemp1;
            string updatedTextTemp2;
            result = TryRegexUpdate(original, dummy, out updatedTextTemp1, wholeWord);
            result = TryRegexUpdate(original, item, out updatedTextTemp2, wholeWord);

            if (result)
            {
                if (ContainsTags(updatedTextTemp2))
                {
                    var match1 = Regex.Match(updatedTextTemp1, "(<.+?>)(.+?)(</.+?>)");
                    var match2 = Regex.Match(updatedTextTemp2, "(<.+?>)(.+?)(</.+?>)");

                    if (match1.Success && match2.Success)
                    {
                        updatedText = match1.Groups[1].Value +
                                      match2.Groups[2].Value +
                                      match1.Groups[3].Value;
                    }
                    else
                    {
                        // By setting this to updatedTextTemp1 we ignore ToUpper, ToLower and Strconv
                        // 
                        updatedText = updatedTextTemp1;
                    }
                }
                else
                {
                    // If it doesn't contain tags, we just use the normal version
                    updatedText = updatedTextTemp2;
                }
            }

            return result;
        }

        private bool TryReplaceString(string original, ConversionItem item, SearchText search, out string updatedText)
        {
            var result = false;

            if (search.StrConv)
            {
                result = TryStrConvUpdate(original, item, out updatedText);
            }
            else if (search.UseRegex)
            {
                result = TryRegexUpdate(original, item, out updatedText);
            }
            else if (search.WholeWord)
            {
                result = TryRegexUpdate(original, item, out updatedText, true);
            }
            else
            {
                result = TryNormalStringUpdate(original, item, out updatedText);
            }

            return result;
        }

        private bool TryStrConvUpdate(string original, ConversionItem item, out string updatedText)
        {
            var search = item.Search;
            var replacement = item.Replacement;
            updatedText = null;

            if (SearchTextValid(search))
            {
                VbStrConv vbStrConv = ConvertToEnum(search);

                try
                {
                    if (settings.SourceCulture != null)
                    {
                        updatedText = original.RegexReplace(search.Text, replacement, search.CaseSensitive,
                                                            m => Strings.StrConv(m.Value, vbStrConv, settings.SourceCulture.LCID));
                    }
                    else if (vbStrConv.HasFlag(VbStrConv.Hiragana) || vbStrConv.HasFlag(VbStrConv.Katakana))
                    {
                        updatedText = original.RegexReplace(search.Text, replacement, search.CaseSensitive,
                                                            m => Strings.StrConv(m.Value, vbStrConv, new CultureInfo("ja-JP").LCID));
                    }
                    else
                    {
                        updatedText = original.RegexReplace(search.Text, replacement, search.CaseSensitive,
                                                            m => Strings.StrConv(m.Value, vbStrConv));
                    }
                }
                catch (ArgumentException)
                {
                    Reporter.Report(this, ErrorLevel.Warning, "Regular expression error", $"{search.Text}");
                }
            }

            // String compare has to be always case-sensitive here
            return (updatedText != null) && !original.NormalStringCompare(updatedText, true, settings.SourceCulture);
        }

        private void UpdateIText(string updatedText, IText itext)
        {
            // updatedText could be empty if string was removed
            Contract.Requires<ArgumentNullException>(updatedText != null);
            Contract.Requires<ArgumentNullException>(itext != null);

            if (updatedText != string.Empty)
            {
                itext.Properties.Text = updatedText;
            }
            else
            {
                // If string is empty, remove the IText from the container
                itext.RemoveFromParent();
            }
        }

        private void UpdateTagContent(string updatedText, ITagPair tagPair)
        {
            var match = Regex.Match(updatedText, "(<.+?>)(.+?)(</.+?>)");

            if (match.Success)
            {
                var input = string.Concat("<root>", match.Groups[2].Value, "</root>");

                var markupData = CreateMarkupData(input);

                tagPair.Clear();

                foreach (var item in markupData)
                {
                    tagPair.Add(item);
                }
            }
            else
            {
                Reporter.Report(this, ErrorLevel.Error, "Could not update tag content", $"Error: {updatedText}");
            }
        }

        private void UpdateTagPair(string updatedText, string fullText, ITagPair tagPair)
        {
            var parent = tagPair.Parent;

            if (ContainsTags(updatedText))
            {
                try
                {
                    // Check if element name changed
                    if (TagsChanged(updatedText, fullText))
                    {
                        ReplaceTagPair(updatedText, tagPair, parent);
                    }
                    else
                    {
                        // Check if the attributes changed
                        if (AttributesChanged(updatedText, fullText))
                        {
                            var attributes = XElement.Parse(updatedText).Attributes();
                            var formatting = tagPair.StartTagProperties.Formatting;

                            var match = Regex.Match(updatedText, "(<.+?>)(.+?)(</.+?>)");
                            var oldTagContent = tagPair.StartTagProperties.TagContent.Split(new[] { ' ', '>' });

                            tagPair.StartTagProperties.DisplayText = match.Groups[1].Value;
                            tagPair.StartTagProperties.TagContent = match.Groups[1].Value;

                            foreach (var attrib in attributes)
                            {
                                var formattingItem = FormattingFactory.CreateFormatting(attrib.Name.LocalName, attrib.Value);
                                formatting.Add(formattingItem);

                                var metaDataList = new List<KeyValuePair<string, string>>();
                                foreach (var data in tagPair.TagProperties.MetaData)
                                {
                                    foreach (var piece in oldTagContent)
                                    {
                                        var m = Regex.Match(piece, "(.+?)=\"(.+?)\"");
                                        if (m.Success)
                                        {
                                            var updated = data.Value.Replace($"\"{m.Groups[2].Value}\"", $"\"{attrib.Value}\"");

                                            if (!ReferenceEquals(data.Value, updated))
                                            {
                                                metaDataList.Add(new KeyValuePair<string, string>(data.Key, updated));
                                            }
                                        }
                                    }
                                }

                                foreach (var pair in metaDataList)
                                {
                                    tagPair.TagProperties.SetMetaData(pair.Key, pair.Value);
                                }
                            }
                        }

                        if (ContentChanged(updatedText, fullText))
                        {
                            UpdateTagContent(updatedText, tagPair);
                        }
                    }
                }
                catch (ArgumentException)
                {
                    Reporter.Report(this, ErrorLevel.Error, "Error parsing updated text. Xml invalid.", $"Error: {updatedText}");
                }
            }
            else
            {
                var itext = CreateIText(updatedText);
                parent.Add(itext);

                tagPair.RemoveFromParent();
            }
        }

        private void VisitChildren(IAbstractMarkupDataContainer container)
        {
            Contract.Requires<ArgumentNullException>(container != null);

            foreach (var item in container)
            {
                item.AcceptVisitor(this);
            }
        }

        #region Not Used

        public void VisitCommentMarker(ICommentMarker commentMarker)
        {
        }

        public void VisitLocationMarker(ILocationMarker location)
        {
        }

        public void VisitLockedContent(ILockedContent lockedContent)
        {
        }

        public void VisitOtherMarker(IOtherMarker marker)
        {
        }

        /// <summary>
        /// Placeholder tags are ignored, as the translatable content
        /// is assumed to be fixed
        /// </summary>
        /// <param name="tag"><see cref="IPlaceholderTag"/></param>
        public void VisitPlaceholderTag(IPlaceholderTag tag)
        {
        }

        public void VisitRevisionMarker(IRevisionMarker revisionMarker)
        {
        }

        #endregion Not Used
    }
}