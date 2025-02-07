using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RDGRBotV2.Services.Interfaces
{
    public interface IRandomProvider
    {
        Random Instance { get; }
    }
}
