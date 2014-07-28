using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ClearCanvas.Common;

namespace ClearCanvas.Dicom.Utilities
{
    /// <summary>
    /// <para>
    /// A class to encode a representation of a DICOM object in an XML form,
    /// suitable for analysis as human-readable text, or for feeding into an
    /// XSLT-based translation, and to convert them back again.
    /// </para>
    /// </summary>
    /// <example>
    /// <para>A typical example of how to invoke this class to convert DICOM to XML would be:</para>
    /// XmlDicomObject.CreateXmlDocument("dicomfile.dcm", "xmlfile.xml");
    /// <para>A typical example of converting XML back to DICOM would be:</para>
    /// XmlDicomObject.CreateDicomFile("DicomFile.dcm", "DicomObject.xml");
    /// </example>
    /// <remarks>
    /// <para>
    /// There are a number of characteristics of this form of output:
    /// </para>
    ///<ul>
    /// <li>Rather than a generic name for all DICOM data elements, like "element", with an attribute to provide the human-readable name,
    /// the name of the XML element itself is a human-readable keyword, as used in the DICOM Data Dictionary for the toolkit;
    /// the group and element tags are available as attributes of each such element; this makes construction of XPath accessors more straightforward.</li>
    /// <li>The value representation of the DICOM source element is conveyed explicitly in an attribute; this facilitates validation of the XML result
    /// (e.g., that the correct VR has been used, and that the values are compatible with that VR).</li>
    /// <li>Individual values of a DICOM data element are expressed as separate XML elements (named "value"), each with an attribute ("number") to specify their order, starting from 1 increasing by 1;
    /// this prevents users of the XML form from needing to parse multiple string values and separate out the DICOM value delimiter (backslash), and allows
    /// XPath accessors to obtain specific values; it also allows for access to separate values of binary, rather than string, DICOM data elements, which
    /// are represented the same way. Within each "value" element, the XML plain character data contains a string representation of the value.</li>
    /// <li>Sequence items are encoded in a similar manner to multi-valued attributes, i.e., there is a nested XML data element (called "Item") with an
    /// explicit numeric attribute ("number") to specify their order, starting from 1 increasing by 1.</li>
    /// </ul>
    /// <para> E.g., to test if an image is original, which is determined by a specific value of <code>ImageType (0008,0008)</code>, one
    /// could write in XPath <code>"/DicomObject/ImageType/value[@number=1] = 'ORIGINAL'"</code>.
    /// </para>
    /// </remarks>
    public class XmlDicomObject
    {
        public static void CreateXmlDocument(DicomAttributeCollection dicomAttributeCollection, string fileName, bool includeBinaryData = false)
        {
            using (var fileStream = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.Write))
            {
                write(fileStream, new XmlDicomObject().GetXmlDocument(dicomAttributeCollection, includeBinaryData));
            }
        }

        public static void CreateXmlDocument(string dicomFileName, string xmlFileName, bool includeBinaryData = false)
        {
            if (string.IsNullOrEmpty(dicomFileName) && !File.Exists(dicomFileName)) return;
            using (var fileStream = new FileStream(xmlFileName, FileMode.OpenOrCreate, FileAccess.Write))
            {
                var fileReader = new DicomFile(dicomFileName);
                fileReader.Load(DicomReadOptions.Default);
                write(fileStream, new XmlDicomObject().GetXmlDocument(fileReader, includeBinaryData));
            }
        }

        public static void CreateDicomFile(string dicomFileName, string xmlFileName)
        {
            if (string.IsNullOrEmpty(xmlFileName) && !File.Exists(xmlFileName)) return;
            using (var fileStream = new FileStream(xmlFileName, FileMode.Open, FileAccess.Read))
            {
                var dicomAttributes = new XmlDicomObject().GetDicomAttributes(fileStream);
                var dicomFileWriter = new DicomFile(dicomFileName, dicomAttributes.Copy(true, true, true, 0x0002FFFF),
                                                    dicomAttributes);
                dicomFileWriter.Save();
            }
        }

        public static DicomAttributeCollection GetDicomAttributes(string xmlFileName)
        {
            if (string.IsNullOrEmpty(xmlFileName) && !File.Exists(xmlFileName)) return null;
            using(var fileStream = new FileStream(xmlFileName, FileMode.Open, FileAccess.Read))
            {
                return new XmlDicomObject().GetDicomAttributes(fileStream);
            }
        }

        public static string ToString(XmlNode node, int indent)
        {
            var str = new StringBuilder();
            for (var i = 0; i < indent; ++i) str.Append("    ");
            str.Append(node);
            if (node.Attributes != null)
            {
                var attrs = node.Attributes;
                for (var j = 0; j < attrs.Count; ++j)
                {
                    var attr = attrs.Item(j);
                    //str.Append(toString(attr,indent+2));
                    str.Append(" ");
                    str.Append(attr);
                }
            }
            str.Append(Environment.NewLine);
            ++indent;
            for (var child = node.FirstChild; child != null; child = child.NextSibling)
            {
                str.Append(ToString(child, indent));
                //str.Append("\n");
            }
            return str.ToString();
        }

        public static string ToString(XmlNode node)
        {
            return ToString(node, 0);
        }

        public XmlDocument GetXmlDocument(DicomAttributeCollection list, bool includeBinaryData)
        {
            var document = new XmlDocument { PreserveWhitespace = false };
            var element = document.CreateElement("DicomObject");
            document.AppendChild(element);
            addXmlNodeFromDicomAttributes(list, document, element, includeBinaryData);
            return document;
        }

        public XmlDocument GetXmlDocument(DicomFile dicomFile, bool includeBinaryData)
        {
            var document = new XmlDocument { PreserveWhitespace = false };
            var element = document.CreateElement("DicomObject");
            document.AppendChild(element);
            addXmlNodeFromDicomAttributes(dicomFile.MetaInfo, document, element, includeBinaryData);
            addXmlNodeFromDicomAttributes(dicomFile.DataSet, document, element, includeBinaryData);
            return document;
        }

        public DicomAttributeCollection GetDicomAttributes(XmlDocument document)
        {
            try
            {
                var list = new DicomAttributeCollection();
                // should be DicomObject
                addAttributesFromXmlNode(list, document.LastChild);
                return list;
            }
            catch (DicomException ex)
            {
                throw ex;
            }

        }

        public DicomAttributeCollection GetDicomAttributes(Stream stream)
        {
            try
            {
                var document = new XmlDocument();
                document.Load(stream);
                return GetDicomAttributes(document);
            }
            catch (IOException io)
            {
                throw io;
            }
            catch (XmlException xe)
            {
                throw xe;
            }
            catch (DicomException de)
            {
                throw de;
            }
        }

        private static void write(Stream outputStream, XmlDocument document)
        {
            // The xml declaration is recommended, but not mandatory
            var xmlDeclaration = document.CreateXmlDeclaration("1.0", "UTF-8", null);
            var root = document.DocumentElement;
            document.InsertBefore(xmlDeclaration, root);
            document.Save(outputStream);
        }

        private string makeElementNameFromHexadecimalGroupElementValues(DicomTag dicomTag)
        {
            var str = new StringBuilder();
            str.Append("HEX"); // XML element names not allowed to start with a number
            var groupString = dicomTag.Group.ToString("X4");
            for (var i = groupString.Length; i < 4; ++i) str.Append("0");
            str.Append(groupString);
            var elementString = dicomTag.Element.ToString("X4");
            for (var i = elementString.Length; i < 4; ++i) str.Append("0");
            str.Append(elementString);
            return str.ToString();
        }

        private void addAttributesFromXmlNode(IDicomAttributeProvider attributeProvider, XmlNode parentNode)
        {
            if (parentNode == null) return;
            var node = parentNode.FirstChild;
            while (node != null)
            {
                var elementName = node.Name;
                var attributes = node.Attributes;
                if (attributes != null)
                {
                    var vrNode = attributes.GetNamedItem("vr");
                    if (vrNode != null)
                    {
                        var vrString = vrNode.Value;
                        if (vrString != null)
                        {
                            var dicomTag = DicomTagDictionary.GetDicomTag(elementName);
                            //if ((group % 2 == 0 && element == 0) || (group == 0x0008 && element == 0x0001) || (group == 0xfffc && element == 0xfffc))
                            //{
                            //    Platform.Log(LogLevel.Error, "ignoring group length or length to end or dataset trailing padding " + dicomTag);
                            //}
                            //else
                            //{
                            if (vrString == "SQ")
                            {
                                if (node.HasChildNodes)
                                {
                                    var childNode = node.FirstChild;
                                    while (childNode != null)
                                    {
                                        var childNodeName = childNode.Name;
                                        if (!string.IsNullOrWhiteSpace(childNodeName) && childNodeName == "Item")
                                        {
                                            var itemList = new DicomSequenceItem();
                                            addAttributesFromXmlNode(itemList, childNode);
                                            attributeProvider[dicomTag].AddSequenceItem(itemList);
                                        }
                                        childNode = childNode.NextSibling;
                                    }
                                }
                            }
                            else
                            {
                                if (node.HasChildNodes)
                                {
                                    var childNode = node.FirstChild;
                                    while (childNode != null)
                                    {
                                        var childNodeName = childNode.Name;
                                        // Cleanup the common XML character replacements
                                        if (!string.IsNullOrWhiteSpace(childNodeName) && childNodeName == "value")                                            
                                            attributeProvider[dicomTag].AppendString(XmlUnescapeString(childNode.InnerText));
                                        // else may be a #text element in between
                                        childNode = childNode.NextSibling;
                                    }
                                }
                                else if (dicomTag != null)
                                    attributeProvider[dicomTag].SetEmptyValue();
                            }
                            //}
                        }
                    }
                }
                node = node.NextSibling;
            }
        }

        private void addXmlNodeFromDicomAttributes(IEnumerable<DicomAttribute> attributes, XmlDocument document, XmlNode parentNode, bool includeBinaryData)
        {
            foreach (var attribute in attributes)
            {
                var tag = attribute.Tag;

                var elementName = tag.VariableName;
                if (string.IsNullOrWhiteSpace(elementName))
                {
                    elementName = makeElementNameFromHexadecimalGroupElementValues(tag);
                }
                var node = document.CreateElement(elementName);
                parentNode.AppendChild(node);

                {
                    var attr = document.CreateAttribute("group");
                    attr.Value = tag.Group.ToString("X4");
                    node.Attributes.SetNamedItem(attr);
                }
                {
                    var attr = document.CreateAttribute("element");
                    attr.Value = tag.Element.ToString("X4");
                    node.Attributes.SetNamedItem(attr);
                }
                {
                    var attr = document.CreateAttribute("vr");
                    attr.Value = attribute.Tag.VR.ToString();
                    node.Attributes.SetNamedItem(attr);
                }

                if (attribute is DicomAttributeSQ)
                {
                    var si = attribute.Values as DicomSequenceItem[];
                    var count = 0;
                    if (si != null)
                        foreach (var item in si)
                        {
                            var itemNode = document.CreateElement("Item");
                            var numberAttr = document.CreateAttribute("number");
                            numberAttr.Value = (++count).ToString(CultureInfo.InvariantCulture);
                            itemNode.Attributes.SetNamedItem(numberAttr);
                            node.AppendChild(itemNode);
                            addXmlNodeFromDicomAttributes(item, document, itemNode, includeBinaryData);
                        }
                }
                else
                {
                    try
                    {
                        addValuesXmlNode(attribute, document, node, includeBinaryData);
                    }
                    catch (DicomException ex)
                    {
                        Platform.Log(LogLevel.Error, "Error while adding xml node from Dicom attribute " + ex);
                    }

                }
            }
        }

        private static void addValuesXmlNode(DicomAttribute attribute, XmlDocument document, XmlNode node, bool includeBinaryData)
        {
            if (attribute.Values == null) return;
            var values = attribute.Values as string[];
            if (values != null) addValuesXmlNode(values, document, node, true);
            var ushortValues = attribute.Values as ushort[];
            if (ushortValues != null) addValuesXmlNode(ushortValues, document, node);
            var uintValues = attribute.Values as uint[];
            if (uintValues != null) addValuesXmlNode(uintValues, document, node);
            if (!includeBinaryData) return;
            var byteValues = attribute.Values as byte[];
            if (byteValues != null) addValuesXmlNode(byteValues, document, node);
        }

        private static void addValuesXmlNode<T>(IList<T> values, XmlDocument document, XmlNode node, bool checkControlCharacters = false)
        {
            for (var j = 0; j < values.Count; ++j)
                addValuexmlNode(document, values[j].ToString(), (j + 1).ToString(CultureInfo.InvariantCulture), node, checkControlCharacters);
        }

        private static void addValuexmlNode(XmlDocument document, string value, string index, XmlNode node, bool checkControlCharacters)
        {
            var valueNode = document.CreateElement("value");
            var numberAttr = document.CreateAttribute("number");
            numberAttr.Value = index;
            valueNode.Attributes.SetNamedItem(numberAttr);
            valueNode.AppendChild(document.CreateTextNode(checkControlCharacters ? XmlEscapeString(value) : value));
            node.AppendChild(valueNode);
        }

        private static string XmlEscapeString(string input)
        {
            string result = input ?? string.Empty;

            result = SecurityElement.Escape(result);

            // Do the regular expression to escape out other invalid XML characters in the string not caught by the above.
            // NOTE: the \x sequences you see below are C# escapes, not Regex escapes
            result = Regex.Replace(result, "[^\x9\xA\xD\x20-\xFFFD]", m => string.Format("&#x{0:X};", (int)m.Value[0]));

            return result;
        }

        private static string XmlUnescapeString(string input)
        {
            string result = input ?? string.Empty;

            // unescape any value-encoded XML entities
            result = Regex.Replace(result, "&#[Xx]([0-9A-Fa-f]+);", m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.AllowHexSpecifier)).ToString());
            result = Regex.Replace(result, "&#([0-9]+);", m => ((char)int.Parse(m.Groups[1].Value)).ToString());

            // unescape any entities encoded by SecurityElement.Escape (only <>'"&)
            result = result.Replace("&lt;", "<").
                Replace("&gt;", ">").
                Replace("&quot;", "\"").
                Replace("&apos;", "'").
                Replace("&amp;", "&");

            return result;
        }
    }
}
