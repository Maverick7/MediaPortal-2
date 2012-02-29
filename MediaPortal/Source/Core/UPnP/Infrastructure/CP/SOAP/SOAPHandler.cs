#region Copyright (C) 2007-2012 Team MediaPortal

/*
    Copyright (C) 2007-2012 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using UPnP.Infrastructure.Common;
using UPnP.Infrastructure.CP.DeviceTree;
using UPnP.Infrastructure.Utils;

namespace UPnP.Infrastructure.CP.SOAP
{
  /// <summary>
  /// Soap message encoding and decoding class for the UPnP control point.
  /// </summary>
  public class SOAPHandler
  {
    protected static IList<object> EMPTY_OBJECT_LIST;

    static SOAPHandler()
    {
      EMPTY_OBJECT_LIST = new List<object>();
    }

    /// <summary>
    /// Encodes a call of the specified <paramref name="action"/> with the given <paramref name="inParamValues"/> and
    /// returns the resulting SOAP XML string.
    /// </summary>
    /// <param name="action">Action to be called.</param>
    /// <param name="inParamValues">List of parameter values which must match the action's signature.
    /// Can be <c>null</c> if the parameter list is empty.</param>
    /// <param name="upnpVersion">UPnP version to use for the encoding.</param>
    /// <returns>XML string which contains the SOAP document.</returns>
    public static string EncodeCall(CpAction action, IList<object> inParamValues, UPnPVersion upnpVersion)
    {
      bool targetSupportsUPnP11 = upnpVersion.VerMin >= 1;
      StringBuilder result = new StringBuilder(5000);
      using (StringWriterWithEncoding stringWriter = new StringWriterWithEncoding(result, Encoding.UTF8))
      using (XmlWriter writer = XmlWriter.Create(stringWriter, UPnPConfiguration.DEFAULT_XML_WRITER_SETTINGS))
      {
        SoapHelper.WriteSoapEnvelopeStart(writer, true);
        writer.WriteStartElement("u", action.Name, action.ParentService.ServiceTypeVersion_URN);

        // Check input parameters
        IList<CpArgument> formalArguments = action.InArguments;
        if (inParamValues == null)
          inParamValues = EMPTY_OBJECT_LIST;
        if (inParamValues.Count != formalArguments.Count)
          throw new ArgumentException("Invalid argument count");
        for (int i = 0; i < formalArguments.Count; i++)
        {
          CpArgument argument = formalArguments[i];
          object value = inParamValues[i];
          writer.WriteStartElement(argument.Name);
          argument.SoapSerializeArgument(value, !targetSupportsUPnP11, writer);
          writer.WriteEndElement(); // argument.Name
        }
        SoapHelper.WriteSoapEnvelopeEndAndClose(writer);
      }
      return result.ToString();
    }

    /// <summary>
    /// Takes the XML document provided by the given <paramref name="body"/> stream, parses it in the given
    /// <paramref name="contentEncoding"/> and provides the action result to the appropriate receiver.
    /// </summary>
    /// <param name="body">Body stream of the SOAP XML action result message.</param>
    /// <param name="contentEncoding">Encoding of the body stream.</param>
    /// <param name="action">Action which was called before.</param>
    /// <param name="clientState">State object which was given in the action call and which will be returned to the client.</param>
    /// <param name="upnpVersion">UPnP version of the UPnP server.</param>
    public static void HandleResult(Stream body, Encoding contentEncoding, CpAction action, object clientState, UPnPVersion upnpVersion)
    {
      bool sourceSupportsUPnP11 = upnpVersion.VerMin >= 1;
      IList<object> outParameterValues;
      try
      {
        if (!body.CanRead)
        {
          UPnPConfiguration.LOGGER.Error("SOAPHandler: Empty action result document");
          action.ActionErrorResultPresent(new UPnPError(501, "Invalid server result"), clientState);
          return;
        }
        using (TextReader textReader = new StreamReader(body, contentEncoding))
          outParameterValues = ParseResult(textReader, action, sourceSupportsUPnP11);
      }
      catch (Exception e)
      {
        UPnPConfiguration.LOGGER.Error("SOAPHandler: Error parsing action result document", e);
        action.ActionErrorResultPresent(new UPnPError(501, "Invalid server result"), clientState);
        return;
      }
      try
      {
        // Invoke action result
        action.ActionResultPresent(outParameterValues, clientState);
      }
      catch (Exception e)
      {
        UPnPConfiguration.LOGGER.Error("UPnP subsystem: Error invoking action '{0}'", e, action.FullQualifiedName);
      }
    }

    public static void HandleErrorResult(TextReader textReader, CpAction action, object clientState)
    {
      try
      {
        uint errorCode;
        string errorDescription;
        ParseFaultDocument(textReader, out errorCode, out errorDescription);
        action.ActionErrorResultPresent(new UPnPError(errorCode, errorDescription), clientState);
      }
      catch (Exception e)
      {
        UPnPConfiguration.LOGGER.Error("SOAPHandler: Error parsing action error result document", e);
        ActionFailed(action, clientState, "Invalid server result");
      }
    }

    public static void ActionFailed(CpAction action, object clientState, string errorDescription)
    {
      action.ActionErrorResultPresent(new UPnPError(501, errorDescription), clientState);
    }

    protected static void ParseFaultDocument(TextReader textReader, out uint errorCode, out string errorDescription)
    {
      using(XmlReader reader = XmlReader.Create(textReader, UPnPConfiguration.DEFAULT_XML_READER_SETTINGS))
      {
        reader.MoveToContent();
        // Parse SOAP envelope
        reader.ReadStartElement("Envelope", UPnPConsts.NS_SOAP_ENVELOPE);
        reader.ReadStartElement("Body", UPnPConsts.NS_SOAP_ENVELOPE);
        reader.ReadStartElement("Fault", UPnPConsts.NS_SOAP_ENVELOPE);
        string faultcode = reader.ReadElementString("faultcode");
        int index = faultcode.IndexOf(':');
        if (index == -1 || reader.LookupNamespace(faultcode.Substring(0, index)) != UPnPConsts.NS_SOAP_ENVELOPE ||
            faultcode.Substring(index + 1) != "Client")
          throw new ArgumentException("Invalid faultcode value");
        string faultstring = reader.ReadElementString("faultstring");
        if (faultstring != "UPnPError")
          throw new ArgumentException("Invalid faultstring value (must be 'UPnPError')");
        reader.ReadStartElement("detail");
        reader.ReadStartElement("UPnPError", UPnPConsts.NS_UPNP_CONTROL);
        errorCode = (uint) reader.ReadElementContentAsInt("errorCode", UPnPConsts.NS_UPNP_CONTROL);
        errorDescription = reader.ReadElementString("errorDescription", UPnPConsts.NS_UPNP_CONTROL);
      }
    }

    protected static IList<object> ParseResult(TextReader textReader, CpAction action, bool sourceSupportsUPnP11)
    {
      IList<object> outParameterValues = new List<object>();
      using(XmlReader reader = XmlReader.Create(textReader, UPnPConfiguration.DEFAULT_XML_READER_SETTINGS))
      {
        reader.MoveToContent();
        // Parse SOAP envelope
        reader.ReadStartElement("Envelope", UPnPConsts.NS_SOAP_ENVELOPE);
        reader.ReadStartElement("Body", UPnPConsts.NS_SOAP_ENVELOPE);
        // Reader is positioned at the action element
        string serviceTypeVersion_URN = reader.NamespaceURI;
        string type;
        int version;
        // Parse service and action
        if (!ParserHelper.TryParseTypeVersion_URN(serviceTypeVersion_URN, out type, out version))
          throw new ArgumentException("Invalid service type or version");
        string actionName = reader.LocalName;
        if (!actionName.EndsWith("Response") ||
            actionName.Substring(0, actionName.Length - "Response".Length) != action.Name)
          throw new ArgumentException("Invalid action name in result message");

        IEnumerator<CpArgument> formalArgumentEnumer = action.OutArguments.GetEnumerator();
        if (!SoapHelper.ReadEmptyStartElement(reader))
          // Parse and check output parameters
          while (reader.NodeType != XmlNodeType.EndElement)
          {
            string argumentName = reader.Name; // Arguments don't have a namespace, so take full name
            if (!formalArgumentEnumer.MoveNext()) // Too many arguments
              throw new ArgumentException("Invalid out argument count");
            if (formalArgumentEnumer.Current.Name != argumentName)
              throw new ArgumentException("Invalid argument name");
            object value;
            if (SoapHelper.ReadNull(reader))
              value = null;
            else
              formalArgumentEnumer.Current.SoapParseArgument(reader, !sourceSupportsUPnP11, out value);
            outParameterValues.Add(value);
          }
        if (formalArgumentEnumer.MoveNext()) // Too few arguments
          throw new ArgumentException("Invalid out argument count");
      }
      return outParameterValues;
    }
  }
}
