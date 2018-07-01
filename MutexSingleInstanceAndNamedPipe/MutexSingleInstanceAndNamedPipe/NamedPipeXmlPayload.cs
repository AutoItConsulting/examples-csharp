//  
// Copyright (c) AutoIt Consulting Ltd. All rights reserved.  
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.  
// using System;
//

using System.Collections.Generic;
using System.Xml.Serialization;

namespace MutexSingleInstanceAndNamedPipe
{
    public class NamedPipeXmlPayload
    {
        /// <summary>
        ///     If set to true this is simply a request to terminate
        /// </summary>
        [XmlElement("SignalQuit")]
        public bool SignalQuit { get; set; }

        /// <summary>
        ///     A list of command line arguments.
        /// </summary>
        [XmlElement("CommandLineArguments")]
        public List<string> CommandLineArguments { get; set; } = new List<string>();
    }
}