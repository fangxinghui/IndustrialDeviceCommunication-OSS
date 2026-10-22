using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using DeviceCommunication.Models;

namespace DeviceCommunication.Infrastructure
{
    /// <summary>
    /// Loads and deserializes Controller.xml from embedded resource, file, string, or stream.
    /// </summary>
    public static class ControllerCatalogLoader
    {
        public const string EmbeddedResourceName = "DeviceCommunication.Resources.Controller.xml";

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ControllerCatalog));

        public static ControllerCatalog LoadEmbedded()
        {
            var assembly = typeof(ControllerCatalogLoader).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
            if (stream == null)
            {
                var available = string.Join(", ", assembly.GetManifestResourceNames());
                throw new InvalidOperationException(
                    "Embedded resource '" + EmbeddedResourceName + "' was not found. Available: " + available);
            }

            return Load(stream);
        }

        public static ControllerCatalog TryLoadEmbedded()
        {
            try
            {
                return LoadEmbedded();
            }
            catch
            {
                return Empty();
            }
        }

        public static ControllerCatalog LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is required.", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("Controller catalog XML not found.", path);

            using var stream = File.OpenRead(path);
            return Load(stream);
        }

        public static ControllerCatalog LoadFromXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return Empty();

            using var reader = new StringReader(xml);
            return Deserialize(reader);
        }

        public static ControllerCatalog Load(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            return Deserialize(reader);
        }

        public static ControllerCatalog Load(Stream stream, Encoding encoding)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            return Deserialize(reader);
        }

        public static ControllerType ToControllerType(ControllerKind kind)
        {
            switch (kind)
            {
                case ControllerKind.Plc: return ControllerType.PLC;
                case ControllerKind.Motion: return ControllerType.MotionalCard;
                case ControllerKind.Analog: return ControllerType.AnalogModule;
                case ControllerKind.DigitalIo: return ControllerType.DigitalIoModule;
                case ControllerKind.Daq: return ControllerType.DataAcquisition;
                default: return ControllerType.DigitalIoModule;
            }
        }

        private static ControllerCatalog Deserialize(TextReader reader)
        {
            var catalog = Serializer.Deserialize(reader) as ControllerCatalog;
            if (catalog == null)
                return Empty();
            if (catalog.Controllers == null)
                catalog.Controllers = new ObservableCollection<ControllerCategoryItem>();
            return catalog;
        }

        private static ControllerCatalog Empty()
        {
            return new ControllerCatalog
            {
                Controllers = new ObservableCollection<ControllerCategoryItem>()
            };
        }
    }
}