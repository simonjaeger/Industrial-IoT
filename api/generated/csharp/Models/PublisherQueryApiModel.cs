// <auto-generated>
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for
// license information.
//
// Code generated by Microsoft (R) AutoRest Code Generator 1.0.0.0
// Changes may cause incorrect behavior and will be lost if the code is
// regenerated.
// </auto-generated>

namespace Microsoft.Azure.IIoT.Opc.Registry.Models
{
    using Newtonsoft.Json;
    using System.Linq;

    /// <summary>
    /// Publisher registration query
    /// </summary>
    public partial class PublisherQueryApiModel
    {
        /// <summary>
        /// Initializes a new instance of the PublisherQueryApiModel class.
        /// </summary>
        public PublisherQueryApiModel()
        {
            CustomInit();
        }

        /// <summary>
        /// Initializes a new instance of the PublisherQueryApiModel class.
        /// </summary>
        /// <param name="siteId">Site of the publisher</param>
        /// <param name="connected">Included connected or disconnected</param>
        public PublisherQueryApiModel(string siteId = default(string), bool? connected = default(bool?))
        {
            SiteId = siteId;
            Connected = connected;
            CustomInit();
        }

        /// <summary>
        /// An initialization method that performs custom operations like setting defaults
        /// </summary>
        partial void CustomInit();

        /// <summary>
        /// Gets or sets site of the publisher
        /// </summary>
        [JsonProperty(PropertyName = "siteId")]
        public string SiteId { get; set; }

        /// <summary>
        /// Gets or sets included connected or disconnected
        /// </summary>
        [JsonProperty(PropertyName = "connected")]
        public bool? Connected { get; set; }

    }
}
