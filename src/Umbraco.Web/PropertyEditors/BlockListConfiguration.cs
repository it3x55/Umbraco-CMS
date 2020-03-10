﻿using Newtonsoft.Json;
using Umbraco.Core.PropertyEditors;

namespace Umbraco.Web.PropertyEditors
{

    /// <summary>
    /// The configuration object for the Block List editor
    /// </summary>
    public class BlockListConfiguration
    {

        [ConfigurationField("blocks", "Available Blocks", "views/propertyeditors/blocklist/prevalue/blocklist.elementtypepicker.html", Description = "Define the available blocks.")]
        public BlockConfiguration[] Blocks { get; set; }


        [ConfigurationField("validationLimit", "Amount", "numberrange", Description = "Set a required range of blocks")]
        public NumberRange ValidationLimit { get; set; } = new NumberRange();

        public class NumberRange
        {
            [JsonProperty("min")]
            public int? Min { get; set; }

            [JsonProperty("max")]
            public int? Max { get; set; }
        }

        public class BlockConfiguration
        {
            // TODO: rename this to contentElementTypeAlias, I would like this to be specific, since we have the settings.
            [JsonProperty("contentTypeAlias")]
            public string Alias { get; set; }

            [JsonProperty("settingsElementTypeAlias")]
            public string SettingsElementTypeAlias { get; set; }

            [JsonProperty("view")]
            public string View { get; set; }

            [JsonProperty("label")]
            public string Label { get; set; }
        }

        [ConfigurationField("useInlineEditingAsDefault", "Inline editing mode", "boolean", Description = "Use the inline editor as the default block view")]
        public bool UseInlineEditingAsDefault { get; set; }

    }
}