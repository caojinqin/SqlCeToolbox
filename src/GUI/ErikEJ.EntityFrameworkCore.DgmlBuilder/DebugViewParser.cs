﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace DgmlBuilder
{
    public class DebugViewParser
    {
        public DebugViewParserResult Parse(string[] debugViewLines, string dbContextName)
        {
            var result = new DebugViewParserResult();

            var modelAnnotated = false;
            var productVersion = string.Empty;
            var modelAnnotations = string.Empty;
            var modelPropertyAccessMode = "PropertyAccessMode.Default";
            var changeTrackingStrategy = "ChangeTrackingStrategy.Snapshot";

            foreach (var line in debugViewLines)
            {
                if (line.StartsWith("Model:"))
                {
           
                    var props = line.Trim().Split(' ').ToList();
                    if (props.Count > 0)
                    {
                        changeTrackingStrategy = props.Where(p => p.StartsWith("ChangeTrackingStrategy.")).FirstOrDefault();
                        if (string.IsNullOrEmpty(changeTrackingStrategy))
                            changeTrackingStrategy = "ChangeTrackingStrategy.Snapshot";

                        modelPropertyAccessMode = GetPropertyAccessMode(props);
                    }
                }
                if (line.StartsWith("Annotations:"))
                {
                    modelAnnotated = true;
                }
                if (modelAnnotated)
                {
                    if (line.TrimStart().StartsWith("ProductVersion: "))
                    {
                        productVersion = line.Trim().Split(' ')[1];
                    }
                    if (!line.TrimStart().StartsWith("ProductVersion: " ) &&
                        !line.TrimStart().StartsWith("Annotations:"))
                    {
                        modelAnnotations += line.Trim() + Environment.NewLine;
                    }
                }
            }
            result.Nodes.Add(
                $"<Node Id=\"Model\" Label=\"{dbContextName}\" ChangeTrackingStrategy=\"{changeTrackingStrategy}\" PropertyAccessMode=\"{modelPropertyAccessMode}\" ProductVersion=\"{productVersion}\" Annotations=\"{modelAnnotations.Trim()}\" Category=\"Model\" Group=\"Expanded\" />");

            var entityName = string.Empty;
            var properties = new List<string>();
            var propertyLinks = new List<string>();
            var inProperties = false;
            var inOtherProperties = false;
            var i = -1;
            foreach (var line in debugViewLines)
            {
                i++;
                if (line.TrimStart().StartsWith("EntityType:"))
                {
                    entityName = line.Trim().Split(' ')[1];
                    BuildEntity(debugViewLines, entityName, i, result, properties, propertyLinks, line, ref inProperties);
                }
                if (line.TrimStart().StartsWith("Properties:"))
                {
                    inProperties = true;
                    inOtherProperties = false;
                }

                if (!string.IsNullOrEmpty(entityName) && inProperties)
                {
                    if (line.StartsWith("    Keys:")
                    ||  line.StartsWith("    Navigations:")
                    ||  line.StartsWith("    Annotations:")
                    ||  line.StartsWith("    Foreign keys:"))
                    {
                        inOtherProperties = true;
                        continue;
                    }
                    if (line.StartsWith("      ") && !inOtherProperties)
                    {
                        var annotations = GetAnnotations(i, debugViewLines);

                        // Not included in graph for now
                        var navigations = GetNavigationNodes(i, debugViewLines);

                        var foreignKeysFragment = GetForeignKeys(i, debugViewLines);

                        if (line.StartsWith("        Annotations:")
                         || line.StartsWith("          "))
                        {
                            continue;
                        }

                        var annotation = string.Join(Environment.NewLine, annotations);

                        var foundLine = line.Replace("(no field, ", "(nofield,");
                        foundLine = foundLine.Replace(", ", ",");

                        var props = foundLine.Trim().Split(' ').ToList();

                        var name = props[0];
                        var field = GetTypeValue(props[1], true);
                        var type = GetTypeValue(props[1], false);

                        props.RemoveRange(0, 2);

                        var isRequired = props.Contains("Required");
                        var isIndexed = props.Contains("Index");
                        var isPrimaryKey = props.Contains("PK");
                        var isForeignKey = props.Contains("FK");
                        var isShadow = props.Contains("Shadow");
                        var isAlternateKey = props.Contains("AlternateKey");
                        var isConcurrency = props.Contains("Concurrency");
                        var isUnicode = !props.Contains("Ansi");

                        var beforeSaveBehavior = "PropertySaveBehavior.Save";
                        if (props.Contains("BeforeSave:PropertySaveBehavior.Ignore"))
                            beforeSaveBehavior = "PropertySaveBehavior.Ignore";
                        if (props.Contains("BeforeSave:PropertySaveBehavior.Throw"))
                            beforeSaveBehavior = "PropertySaveBehavior.Throw";

                        var afterSaveBehavior = "PropertySaveBehavior.Save";
                        if (props.Contains("AfterSave:PropertySaveBehavior.Ignore"))
                            afterSaveBehavior = "PropertySaveBehavior.Ignore";
                        if (props.Contains("AfterSave:PropertySaveBehavior.Throw"))
                            afterSaveBehavior = "PropertySaveBehavior.Throw";

                        string propertyAccesMode = GetPropertyAccessMode(props);

                        var maxLength = props.FirstOrDefault(p => p.StartsWith("MaxLength"));
                        if (string.IsNullOrEmpty(maxLength))
                        {
                            maxLength = "None";
                        }
                        else
                        {
                            maxLength = maxLength.Replace("MaxLength", string.Empty);
                        }

                        var valueGenerated = props.FirstOrDefault(p => p.StartsWith("ValueGenerated.")) ?? "None";
                        var category = "Property";
                        if (!isRequired) category = "Property Optional";
                        if (isForeignKey) category = "Property Foreign";
                        if (isPrimaryKey) category = "Property Primary";

                        properties.Add(
                            $"<Node Id = \"{entityName}.{name}\" Label=\"{name}\" Name=\"{name}\" Category=\"{category}\" Type=\"{type}\" MaxLength=\"{maxLength}\" Field=\"{field}\" PropertyAccessMode=\"{propertyAccesMode}\" BeforeSaveBehavior=\"{beforeSaveBehavior}\" AfterSaveBehavior=\"{afterSaveBehavior}\" Annotations=\"{annotation}\" IsPrimaryKey=\"{isPrimaryKey}\" IsForeignKey=\"{isForeignKey}\" IsRequired=\"{isRequired}\" IsIndexed=\"{isIndexed}\" IsShadow=\"{isShadow}\" IsAlternateKey=\"{isAlternateKey}\" IsConcurrencyToken=\"{isConcurrency}\" IsUnicode=\"{isUnicode}\" ValueGenerated=\"{valueGenerated}\" />");

                        propertyLinks.Add($"<Link Source = \"{entityName}\" Target=\"{entityName}.{name}\" Category=\"Contains\" />");

                        propertyLinks.AddRange(ParseForeignKeys(foreignKeysFragment));
                    }
                }
            }
            BuildEntity(debugViewLines, entityName, i, result, properties, propertyLinks, null, ref inProperties);
            return result;
        }

        private static string GetPropertyAccessMode(List<string> props)
        {
            var propertyAccesMode = "PropertyAccessMode.Default";
            if (props.Contains("PropertyAccessMode.Field"))
                propertyAccesMode = "PropertyAccessMode.Field";
            if (props.Contains("PropertyAccessMode.FieldDuringConstruction"))
                propertyAccesMode = "PropertyAccessMode.FieldDuringConstruction";
            if (props.Contains("PropertyAccessMode.Property"))
                propertyAccesMode = "PropertyAccessMode.Property";
            return propertyAccesMode;
        }

        private string BuildEntity(string[] debugViewLines, string entityName, int i, DebugViewParserResult result,
            List<string> properties, List<string> propertyLinks, string line, ref bool inProperties)
        {
            if (!string.IsNullOrEmpty(entityName))
            {
                var isAbstract = false;
                var baseClass = string.Empty;
                string changeTrackingStrategy = "ChangeTrackingStrategy.Snapshot";

                if (!string.IsNullOrEmpty(line))
                {
                    var parts = line.Trim().Split(' ').ToList();
                    isAbstract = parts.Contains("Abstract");
                    if (parts.Contains("Base:"))
                    {
                        baseClass = parts[parts.IndexOf("Base:") + 1];
                    }
                    changeTrackingStrategy = parts.Where(p => p.StartsWith("ChangeTrackingStrategy.")).FirstOrDefault();
                }
                if (string.IsNullOrEmpty(changeTrackingStrategy))
                    changeTrackingStrategy = "ChangeTrackingStrategy.Snapshot";

                var annotations = GetEntityAnnotations(i, debugViewLines);
                var annotation = string.Join(Environment.NewLine, annotations);

                result.Nodes.Add(
                    $"<Node Id = \"{entityName}\" Label=\"{entityName}\" Name=\"{entityName}\" BaseClass=\"{baseClass}\" IsAbstract=\"{isAbstract}\" ChangeTrackingStrategy=\"{changeTrackingStrategy}\"  Annotations=\"{annotation}\" Category=\"EntityType\" Group=\"Expanded\" />");
                result.Links.Add(
                    $"<Link Source = \"Model\" Target=\"{entityName}\" Category=\"Contains\" />");
                result.Nodes.AddRange(properties);
                result.Links.AddRange(propertyLinks);
                properties.Clear();
                propertyLinks.Clear();
            }
            if (!string.IsNullOrEmpty(line))
                entityName = line.Trim().Split(' ')[1];
            inProperties = false;
            return entityName;
        }

        private IEnumerable<string> ParseForeignKeys(List<string> foreignKeysFragments)
        {
            var links = new List<string>();
            int i = 0;
            var annotation = new List<string>();
            if (foreignKeysFragments.Count > 1)
            {
                foreach (var foreignKeysFragment in foreignKeysFragments)
                {
                    i++;
                    var trim = foreignKeysFragment.Trim();

                    if (trim == "Foreign keys:") continue;

                    if (trim == "Annotations:")
                    {
                        continue;
                    }

                    annotation = GetFkAnnotations(i, foreignKeysFragments.ToArray());

                    if (trim.StartsWith("Relational:")) continue;

                    //Multi key FKs!
                    trim = trim.Replace("', '", ",");

                    var parts = trim.Split(' ').ToList();

                    for (int x = 0; x < parts.Count; x++)
                    {
                        if (parts[x].StartsWith("{'"))
                        {
                            parts[x] = parts[x].Substring(2);
                        }
                        if (parts[x].EndsWith("'}"))
                        {
                            parts[x] = parts[x].Substring(0, parts[x].LastIndexOf("'}", StringComparison.Ordinal));
                        }
                    }

                    var source = parts[0] + "." + parts[1];
                    var target = parts[3] + "." + parts[4];
                    //TblFavoriteDoctor {'DoctorIdFk', 'LocationIdFk'} -> TblDoctorLocation {'DoctorId', 'LocationId'} ToDependent: TblFavoriteDoctor ToPrincipal: TblDoctorLocation

                    var linkSource = source;
                    var linkTarget = target;

                    if (parts[1].Contains(","))
                    {
                        linkSource = parts[0] + "." + parts[1].Split(',')[0];
                        linkTarget = parts[3] + "." + parts[4].Split(',')[0];
                    }
                    parts.RemoveRange(0, 5);

                    var isUnique = parts.Contains("Unique");

                    links.Add($"<Link Source=\"{linkSource}\" Target=\"{linkTarget}\" Name=\"{source + " -> " + target}\" Annotations=\"{string.Join(Environment.NewLine, annotation)}\" IsUnique=\"{isUnique}\" Label=\"{source + " -> " + target}\" Category=\"Foreign Key\" />");
                    annotation.Clear();
                    //OrderNdc {'NdcId'} -> Ndc {'NdcId'} ToDependent: OrderNdc ToPrincipal: Ndc
                }
            }

            return links;
        }

        private string GetTypeValue(string type, bool asField)
        {
            var i = asField ? 0 : 1;
            var result = type.Replace("(", string.Empty).Replace(")", string.Empty);
            if (result.Contains(","))
            {
                return System.Security.SecurityElement.Escape(result.Split(',')[i]);
            }
            return asField ? string.Empty : System.Security.SecurityElement.Escape(result);
        }

        private List<string> GetForeignKeys(int i, string[] debugViewLines)
        {
            var x = i;
            var navigations = new List<string>();
            var maxLength = debugViewLines.Length - 1;
            bool inNavigations = false;
            while (x++ < maxLength)
            {
                var trim = debugViewLines[x].Trim();
                if (!inNavigations) inNavigations = trim == "Foreign keys:";

                if (debugViewLines[x].StartsWith("    Annotations:")
                    || debugViewLines[x].StartsWith("Annotations:")
                    || trim.StartsWith("EntityType:"))
                {
                    break;
                }
                if (inNavigations) navigations.Add(debugViewLines[x]);
            }

            return navigations;
        }

        private List<string> GetEntityAnnotations(int i, string[] debugViewLines)
        {
            var x = i;
            var values = new List<string>();
            var maxLength = debugViewLines.Length - 1;
            bool inTheMix = false;
            while (x++ < maxLength)
            {
                var trim = debugViewLines[x].Trim();
                if (!inTheMix) inTheMix = debugViewLines[x] == "    Annotations: ";

                if (debugViewLines[x].StartsWith("Annotations:")
                    || trim.StartsWith("EntityType:"))
                {
                    break;
                }
                if (inTheMix && !trim.StartsWith("Annotations:")) values.Add(trim);
            }

            return values;
        }

        private List<string> GetNavigationNodes(int i, string[] debugViewLines)
        {
            var x = i;
            var navigations = new List<string>();
            var maxLength = debugViewLines.Length - 1;
            while (x++ < maxLength && debugViewLines[x] == "    Navigations: ")
            {
                while (x++ < maxLength)
                {
                    var trim = debugViewLines[x].Trim();
                    if (trim.StartsWith("Keys:")
                        || debugViewLines[x].StartsWith("    Annotations:")
                        || debugViewLines[x].StartsWith("Annotations:")
                        || trim.StartsWith("EntityType:")
                        || trim.StartsWith("Foreign Keys:")
                        || trim.StartsWith("Keys:"))
                    {
                        break;
                    }
                    navigations.Add(debugViewLines[x].Trim());
                }
            }

            return navigations;
        }

        private List<string> GetAnnotations(int i, string[] debugViewLines)
        {
            var x = i;
            var annotations = new List<string>();
            var maxLength = debugViewLines.Length - 1;
            if (x++ < maxLength && debugViewLines[x] == "        Annotations: ")
            {
                while (x++ < maxLength && debugViewLines[x].StartsWith("        "))
                {
                    annotations.Add(debugViewLines[x].Trim());
                }
            }

            return annotations;
        }

        private List<string> GetFkAnnotations(int i, string[] debugViewLines)
        {
            var x = i;
            var annotations = new List<string>();
            var maxLength = debugViewLines.Length - 1;
            while (x++ < maxLength)
            {
                if (debugViewLines[x].StartsWith("          "))
                {
                    annotations.Add(debugViewLines[x].Trim());
                }

                if (debugViewLines[x].Substring(7, 1) != " ")
                {
                    break;
                }

                if (debugViewLines[x].StartsWith("    Foreign Keys:"))
                    break;
            }

            return annotations;
        }
    }
}
