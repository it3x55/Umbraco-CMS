﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using Umbraco.Core.Configuration;
using Umbraco.Core.IO;
using Umbraco.Core.Models;
using Umbraco.Core.Packaging.Models;
using Umbraco.Core.Services;

namespace Umbraco.Core.Packaging
{
    internal class PackageInstallation : IPackageInstallation
    {
        private readonly IFileService _fileService;
        private readonly IMacroService _macroService;
        private readonly IPackagingService _packagingService;
        private IConflictingPackageContentFinder _conflictingPackageContentFinder;
        private readonly IPackageExtraction _packageExtraction;

        public PackageInstallation(IPackagingService packagingService, IMacroService macroService,
            IFileService fileService, IPackageExtraction packageExtraction)
        {
            if (packageExtraction != null) _packageExtraction = packageExtraction; 
            else throw new ArgumentNullException("packageExtraction");
            if (macroService != null) _macroService = macroService;
            else throw new ArgumentNullException("macroService");
            if (fileService != null) _fileService = fileService;
            else throw new ArgumentNullException("fileService");
            if (packagingService != null) _packagingService = packagingService;
            else throw new ArgumentNullException("packagingService");
        }


        public IConflictingPackageContentFinder ConflictingPackageContentFinder
        {
            private get
            {
                return _conflictingPackageContentFinder ??
                       (_conflictingPackageContentFinder = new ConflictingPackageContentFinder(_macroService, _fileService));
            }
            set
            {
                if (_conflictingPackageContentFinder != null)
                {
                    throw new PropertyConstraintException("This property already have a value");
                }
                _conflictingPackageContentFinder = value;
            }
        }



        private string _fullpathToRoot;
        public string FullpathToRoot
        {
            private get { return _fullpathToRoot ?? (_fullpathToRoot = GlobalSettings.FullpathToRoot); }
            set
            {

                if (_fullpathToRoot != null)
                {
                    throw new PropertyConstraintException("This property already have a value");
                }

                _fullpathToRoot = value;
            }
        }


        public MetaData GetMetaData(string packageFilePath)
        {
            try
            {
                XElement rootElement = GetConfigXmlElement(packageFilePath);
                return GetMetaData(rootElement);
            }
            catch (Exception e)
            {
                throw new Exception("Error reading " + packageFilePath, e);
            }
        }

        public PreInstallWarnings GetPreInstallWarnings(string packageFilePath)
        {
            try
            {
                XElement rootElement = GetConfigXmlElement(packageFilePath);
                return GetPreInstallWarnings(rootElement);
            }
            catch (Exception e)
            {
                throw new Exception("Error reading " + packageFilePath, e);
            }
        }

        public InstallationSummary InstallPackage(string packageFile, int userId)
        {
            XElement dataTypes;
            XElement languages;
            XElement dictionaryItems;
            XElement macroes;
            XElement files;
            XElement templates;
            XElement documentTypes;
            XElement styleSheets;
            XElement documentSet;
            XElement actions;
            MetaData metaData;
            
            try
            {
                XElement rootElement = GetConfigXmlElement(packageFile);
                PackageStructureSanetyCheck(packageFile, rootElement);
                dataTypes = rootElement.Element(Constants.Packaging.DataTypesNodeName);
                languages = rootElement.Element(Constants.Packaging.LanguagesNodeName);
                dictionaryItems = rootElement.Element(Constants.Packaging.DictionaryItemsNodeName);
                macroes = rootElement.Element(Constants.Packaging.MacrosNodeName);
                files = rootElement.Element(Constants.Packaging.FilesNodeName);
                templates = rootElement.Element(Constants.Packaging.TemplatesNodeName);
                documentTypes = rootElement.Element(Constants.Packaging.DocumentTypesNodeName);
                styleSheets = rootElement.Element(Constants.Packaging.StylesheetsNodeName);
                documentSet = rootElement.Element(Constants.Packaging.DocumentSetNodeName);
                actions = rootElement.Element(Constants.Packaging.ActionsNodeName);

                metaData = GetMetaData(rootElement);
            }
            catch (Exception e)
            {
                throw new Exception("Error reading " + packageFile, e);
            }

            try
            {
                
                return new InstallationSummary
                {
                    MetaData = metaData,
                    DataTypesInstalled =
                        dataTypes == null ? new IDataTypeDefinition[0] : InstallDataTypes(dataTypes, userId),
                    LanguagesInstalled = languages == null ? new ILanguage[0] : InstallLanguages(languages, userId),
                    DictionaryItemsInstalled =
                        dictionaryItems == null ? new IDictionaryItem[0] : InstallDictionaryItems(dictionaryItems),
                    MacrosInstalled = macroes == null ? new IMacro[0] : InstallMacros(macroes, userId),
                    FilesInstalled =
                        packageFile == null
                            ? Enumerable.Empty<KeyValuePair<string, bool>>()
                            : InstallFiles(packageFile, files),
                    TemplatesInstalled = templates == null ? new ITemplate[0] : InstallTemplats(templates, userId),
                    DocumentTypesInstalled =
                        documentTypes == null ? new IContentType[0] : InstallDocumentTypes(documentTypes, userId),
                    StylesheetsInstalled =
                        styleSheets == null ? new IStylesheet[0] : InstallStylesheets(styleSheets, userId),
                    DocumentsInstalled = documentSet == null ? new IContent[0] : InstallDocuments(documentSet, userId),
                    Actions = actions == null ? new PackageAction[0] : GetPackageActions(actions, metaData.Name),

                };
            }
            catch (Exception e)
            {
                throw new Exception("Error installing package " + packageFile, e);
            }
        }




        private XDocument GetConfigXmlDoc(string packageFilePath)
        {
            string filePathInPackage;
            string configXmlContent = _packageExtraction.ReadTextFileFromArchive(packageFilePath,
                Constants.Packaging.PackageXmlFileName, out filePathInPackage);

            return XDocument.Parse(configXmlContent);
        }


        private XElement GetConfigXmlElement(string packageFilePath)
        {
            XDocument document = GetConfigXmlDoc(packageFilePath);
            if (document.Root == null ||
                document.Root.Name.LocalName.Equals(Constants.Packaging.UmbPackageNodeName) == false)
            {
                throw new ArgumentException("xml does not have a root node called \"umbPackage\"", packageFilePath);
            }
            return document.Root;
        }


        internal void PackageStructureSanetyCheck(string packageFilePath)
        {
            XElement rootElement = GetConfigXmlElement(packageFilePath);
            PackageStructureSanetyCheck(packageFilePath, rootElement);
        }

        private void PackageStructureSanetyCheck(string packageFilePath, XElement rootElement)
        {
            
            XElement filesElement = rootElement.Element(Constants.Packaging.FilesNodeName);
            if (filesElement != null)
            {
                IEnumerable<FileInPackageInfo> extractFileInPackageInfos =
                    ExtractFileInPackageInfos(filesElement).ToArray();

                IEnumerable<string> missingFiles =
                    _packageExtraction.FindMissingFiles(packageFilePath,
                        extractFileInPackageInfos.Select(i => i.FileNameInPackage)).ToArray();

                if (missingFiles.Any())
                {
                    throw new Exception("The following file(s) are missing in the package: " +
                                        string.Join(", ", missingFiles.Select(
                                            mf =>
                                            {
                                                FileInPackageInfo fileInPackageInfo =
                                                    extractFileInPackageInfos.Single(fi => fi.FileNameInPackage == mf);
                                                return string.Format("Guid: \"{0}\" Original File: \"{1}\"",
                                                    fileInPackageInfo.FileNameInPackage, fileInPackageInfo.RelativePath);
                                            })));
                }

                IEnumerable<string> dubletFileNames = _packageExtraction.FindDubletFileNames(packageFilePath).ToArray();
                if (dubletFileNames.Any())
                {
                    throw new Exception("The following filename(s) are found more than one time in the package, since the filename is used ad primary key, this is not allowed: " +
                                        string.Join(", ", dubletFileNames));
                }
            }
        }




        private static PackageAction[] GetPackageActions(XElement actionsElement, string packageName)
        {
            if (actionsElement == null) { return new PackageAction[0]; }

            if (string.Equals(Constants.Packaging.ActionsNodeName, actionsElement.Name.LocalName) == false)
            {
                throw new ArgumentException("Must be \"" + Constants.Packaging.ActionsNodeName + "\" as root",
                    "actionsElement");
            }

            return actionsElement.Elements(Constants.Packaging.ActionNodeName)
                .Select(elemet =>
                {
                    XAttribute aliasAttr = elemet.Attribute(Constants.Packaging.AliasNodeNameCapital);
                    if (aliasAttr == null)
                        throw new ArgumentException(
                            "missing \"" + Constants.Packaging.AliasNodeNameCapital + "\" atribute in alias element",
                            "actionsElement");

                    var packageAction = new PackageAction
                    {
                        XmlData = elemet, 
                        Alias = aliasAttr.Value,
                        PackageName = packageName,
                    };


                    XAttribute attr = elemet.Attribute(Constants.Packaging.RunatNodeAttribute);

                    ActionRunAt runAt;
                    if (attr != null && Enum.TryParse(attr.Value, true, out runAt)) { packageAction.RunAt = runAt; }

                    attr = elemet.Attribute(Constants.Packaging.UndoNodeAttribute);

                    bool undo;
                    if (attr != null && bool.TryParse(attr.Value, out undo)) { packageAction.Undo = undo; }


                    return packageAction;
                }).ToArray();
        }

        private IContent[] InstallDocuments(XElement documentsElement, int userId = 0)
        {
            if (string.Equals(Constants.Packaging.DocumentSetNodeName, documentsElement.Name.LocalName) == false)
            {
                throw new ArgumentException("Must be \"" + Constants.Packaging.DocumentSetNodeName + "\" as root",
                    "documentsElement");
            }
            return _packagingService.ImportContent(documentsElement, -1, userId).ToArray();
        }

        private IStylesheet[] InstallStylesheets(XElement styleSheetsElement, int userId = 0)
        {
            if (string.Equals(Constants.Packaging.StylesheetsNodeName, styleSheetsElement.Name.LocalName) == false)
            {
                throw new ArgumentException("Must be \"" + Constants.Packaging.StylesheetsNodeName + "\" as root",
                    "styleSheetsElement");
            }
            return _packagingService.ImportStylesheets(styleSheetsElement, userId).ToArray();
        }

        private IContentType[] InstallDocumentTypes(XElement documentTypes, int userId = 0)
        {
            if (string.Equals(Constants.Packaging.DocumentTypesNodeName, documentTypes.Name.LocalName) == false)
            {
                if (string.Equals(Constants.Packaging.DocumentTypeNodeName, documentTypes.Name.LocalName) == false)
                    throw new ArgumentException(
                        "Must be \"" + Constants.Packaging.DocumentTypesNodeName + "\" as root", "documentTypes");

                documentTypes = new XElement(Constants.Packaging.DocumentTypesNodeName, documentTypes);
            }

            return _packagingService.ImportContentTypes(documentTypes, userId).ToArray();
        }

        private ITemplate[] InstallTemplats(XElement templateElement, int userId = 0)
        {
            if (string.Equals(Constants.Packaging.TemplatesNodeName, templateElement.Name.LocalName) == false)
            {
                throw new ArgumentException("Must be \"" + Constants.Packaging.TemplatesNodeName + "\" as root",
                    "templateElement");
            }
            return _packagingService.ImportTemplates(templateElement, userId).ToArray();
        }


        private IEnumerable<KeyValuePair<string, bool>> InstallFiles(string packageFilePath, XElement filesElement)
        {
            return ExtractFileInPackageInfos(filesElement).Select(fpi =>
            {
                bool existingOverrided = _packageExtraction.CopyFileFromArchive(packageFilePath, fpi.FileNameInPackage,
                    fpi.FullPath);

                return new KeyValuePair<string, bool>(fpi.FullPath, existingOverrided);
            }).ToArray();
        }

        private IMacro[] InstallMacros(XElement macroElements, int userId = 0)
        {
            if (string.Equals(Constants.Packaging.MacrosNodeName, macroElements.Name.LocalName) == false)
            {
                throw new ArgumentException("Must be \"" + Constants.Packaging.MacrosNodeName + "\" as root",
                    "macroElements");
            }
            return _packagingService.ImportMacros(macroElements, userId).ToArray();
        }

        private IDictionaryItem[] InstallDictionaryItems(XElement dictionaryItemsElement)
        {
            if (string.Equals(Constants.Packaging.DictionaryItemsNodeName, dictionaryItemsElement.Name.LocalName) ==
                false)
            {
                throw new ArgumentException("Must be \"" + Constants.Packaging.DictionaryItemsNodeName + "\" as root",
                    "dictionaryItemsElement");
            }
            return _packagingService.ImportDictionaryItems(dictionaryItemsElement).ToArray();
        }

        private ILanguage[] InstallLanguages(XElement languageElement, int userId = 0)
        {
            if (string.Equals(Constants.Packaging.LanguagesNodeName, languageElement.Name.LocalName) == false)
            {
                throw new ArgumentException("Must be \"" + Constants.Packaging.LanguagesNodeName + "\" as root", "languageElement");
            }
            return _packagingService.ImportLanguages(languageElement, userId).ToArray();
        }

        private IDataTypeDefinition[] InstallDataTypes(XElement dataTypeElements, int userId = 0)
        {
            if (string.Equals(Constants.Packaging.DataTypesNodeName, dataTypeElements.Name.LocalName) == false)
            {
                if (string.Equals(Constants.Packaging.DataTypeNodeName, dataTypeElements.Name.LocalName) == false)
                {
                    throw new ArgumentException("Must be \"" + Constants.Packaging.DataTypeNodeName + "\" as root", "dataTypeElements");
                }
            }
            return _packagingService.ImportDataTypeDefinitions(dataTypeElements, userId).ToArray();
        }

        private PreInstallWarnings GetPreInstallWarnings(XElement rootElement)
        {
            XElement files = rootElement.Element(Constants.Packaging.FilesNodeName);
            XElement styleSheets = rootElement.Element(Constants.Packaging.StylesheetsNodeName);
            XElement templates = rootElement.Element(Constants.Packaging.TemplatesNodeName);
            XElement alias = rootElement.Element(Constants.Packaging.MacrosNodeName);
            var conflictingPackageContent = new PreInstallWarnings
            {
                UnsecureFiles = files == null ? new IFileInPackageInfo[0] : FindUnsecureFiles(files),
                ConflictingMacroAliases = alias == null ? new IMacro[0] : ConflictingPackageContentFinder.FindConflictingMacros(alias),
                ConflictingTemplateAliases =
                    templates == null ? new ITemplate[0] : ConflictingPackageContentFinder.FindConflictingTemplates(templates),
                ConflictingStylesheetNames =
                    styleSheets == null ? new IStylesheet[0] : ConflictingPackageContentFinder.FindConflictingStylesheets(styleSheets)
            };

            return conflictingPackageContent;
        }

        private IFileInPackageInfo[] FindUnsecureFiles(XElement fileElement)
        {
            return ExtractFileInPackageInfos(fileElement)
                .Where(IsFileNodeUnsecure).Cast<IFileInPackageInfo>().ToArray();
        }

        private bool IsFileNodeUnsecure(FileInPackageInfo fileInPackageInfo)
        {

            // Should be done with regex :)
            if (fileInPackageInfo.Directory.ToLower().Contains(IOHelper.DirSepChar + "app_code")) return true;
            if (fileInPackageInfo.Directory.ToLower().Contains(IOHelper.DirSepChar + "bin")) return true;

            string extension = Path.GetExtension(fileInPackageInfo.Directory);

            return extension.Equals(".dll", StringComparison.InvariantCultureIgnoreCase);
        }


        private IEnumerable<FileInPackageInfo> ExtractFileInPackageInfos(XElement filesElement)
        {
            if (string.Equals(Constants.Packaging.FilesNodeName, filesElement.Name.LocalName) == false)
            {
                throw new ArgumentException("the root element must be \"Files\"", "filesElement");
            }

            return filesElement.Elements(Constants.Packaging.FileNodeName)
                .Select(e =>
                {
                    XElement guidElement = e.Element(Constants.Packaging.GuidNodeName);
                    if (guidElement == null)
                    {
                        throw new ArgumentException("Missing element \"" + Constants.Packaging.GuidNodeName + "\"",
                            "filesElement");
                    }

                    XElement orgPathElement = e.Element(Constants.Packaging.OrgPathNodeName);
                    if (orgPathElement == null)
                    {
                        throw new ArgumentException("Missing element \"" + Constants.Packaging.OrgPathNodeName + "\"",
                            "filesElement");
                    }

                    XElement orgNameElement = e.Element(Constants.Packaging.OrgNameNodeName);
                    if (orgNameElement == null)
                    {
                        throw new ArgumentException("Missing element \"" + Constants.Packaging.OrgNameNodeName + "\"",
                            "filesElement");
                    }


                    return new FileInPackageInfo
                    {
                        FileNameInPackage = guidElement.Value,
                        FileName = PrepareAsFilePathElement(orgNameElement.Value),
                        RelativeDir = UpdatePathPlaceholders(
                            PrepareAsFilePathElement(orgPathElement.Value)),
                        DestinationRootDir = FullpathToRoot
                    };
                }).ToArray();
        }

        private static string PrepareAsFilePathElement(string pathElement)
        {
            return pathElement.TrimStart(new[] {'\\', '/', '~'}).Replace("/", "\\");
        }


        private MetaData GetMetaData(XElement xRootElement)
        {
            XElement infoElement = xRootElement.Element(Constants.Packaging.InfoNodeName);

            if (infoElement == null)
            {
                throw new ArgumentException("Did not hold a \"" + Constants.Packaging.InfoNodeName + "\" element",
                    "xRootElement");
            }

            XElement majorElement = infoElement.XPathSelectElement(Constants.Packaging.PackageRequirementsMajorXpath);
            XElement minorElement = infoElement.XPathSelectElement(Constants.Packaging.PackageRequirementsMinorXpath);
            XElement patchElement = infoElement.XPathSelectElement(Constants.Packaging.PackageRequirementsPatchXpath);
            XElement nameElement = infoElement.XPathSelectElement(Constants.Packaging.PackageNameXpath);
            XElement versionElement = infoElement.XPathSelectElement(Constants.Packaging.PackageVersionXpath);
            XElement urlElement = infoElement.XPathSelectElement(Constants.Packaging.PackageUrlXpath);
            XElement licenseElement = infoElement.XPathSelectElement(Constants.Packaging.PackageLicenseXpath);
            XElement authorNameElement = infoElement.XPathSelectElement(Constants.Packaging.AuthorNameXpath);
            XElement authorUrlElement = infoElement.XPathSelectElement(Constants.Packaging.AuthorWebsiteXpath);
            XElement readmeElement = infoElement.XPathSelectElement(Constants.Packaging.ReadmeXpath);

            XElement controlElement = xRootElement.Element(Constants.Packaging.ControlNodeName);

            int val;

            return new MetaData
            {
                Name = nameElement == null ? string.Empty : nameElement.Value,
                Version = versionElement == null ? string.Empty : versionElement.Value,
                Url = urlElement == null ? string.Empty : urlElement.Value,
                License = licenseElement == null ? string.Empty : licenseElement.Value,
                LicenseUrl =
                    licenseElement == null
                        ? string.Empty
                        : licenseElement.HasAttributes ? licenseElement.AttributeValue<string>("url") : string.Empty,
                AuthorName = authorNameElement == null ? string.Empty : authorNameElement.Value,
                AuthorUrl = authorUrlElement == null ? string.Empty : authorUrlElement.Value,
                Readme = readmeElement == null ? string.Empty : readmeElement.Value,
                ReqMajor = majorElement == null ? 0 : int.TryParse(majorElement.Value, out val) ? val : 0,
                ReqMinor = minorElement == null ? 0 : int.TryParse(minorElement.Value, out val) ? val : 0,
                ReqPatch = patchElement == null ? 0 : int.TryParse(patchElement.Value, out val) ? val : 0,
                Control = controlElement == null ? string.Empty : controlElement.Value
            };
        }

        private static string UpdatePathPlaceholders(string path)
        {
            if (path.Contains("[$"))
            {
                //this is experimental and undocumented...
                path = path.Replace("[$UMBRACO]", SystemDirectories.Umbraco);
                path = path.Replace("[$UMBRACOCLIENT]", SystemDirectories.UmbracoClient);
                path = path.Replace("[$CONFIG]", SystemDirectories.Config);
                path = path.Replace("[$DATA]", SystemDirectories.Data);
            }
            return path;
        }
    }

    public class FileInPackageInfo : IFileInPackageInfo
    {
        public string RelativePath
        {
            get { return Path.Combine(RelativeDir, FileName); }
        }

        public string FileNameInPackage { get; set; }
        public string RelativeDir { get; set; }
        public string DestinationRootDir { private get; set; }

        public string Directory
        {
            get { return Path.Combine(DestinationRootDir, RelativeDir); }
        }

        public string FullPath
        {
            get { return Path.Combine(DestinationRootDir, RelativePath); }
        }

        public string FileName { get; set; }
    }

    public interface IFileInPackageInfo
    {
        string RelativeDir { get; }
        string RelativePath { get; }
        string FileName { get; set; }
    }
    
}