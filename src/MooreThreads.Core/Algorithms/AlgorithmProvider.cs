using System;
using System.Collections.Generic;
using System.Linq;

namespace MooreThreadsUpScaler.Core.Algorithms
{
    public class AlgorithmInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public PerformanceLevel Performance { get; set; }
        public bool SupportsSharpness { get; set; }

        public string PerformanceString => Performance switch
        {
            PerformanceLevel.VeryLow => "Very Low",
            PerformanceLevel.Low     => "Low",
            PerformanceLevel.Medium  => "Medium",
            PerformanceLevel.High    => "High",
            _                        => "Unknown"
        };
    }

    /// <summary>
    /// Registry of all available upscaling algorithms.
    /// Registered as a singleton via DI — not a static class.
    /// </summary>
    public sealed class AlgorithmProvider
    {
        private readonly Dictionary<string, IUpscalingAlgorithm> _algorithms;

        public AlgorithmProvider()
        {
            _algorithms = new Dictionary<string, IUpscalingAlgorithm>(StringComparer.OrdinalIgnoreCase)
            {
                { "LS1",      new LS1Algorithm()      },
                { "LS1Sharp", new LS1SharpAlgorithm() },
                { "FSR",      new FSRAlgorithm()      },
                { "NIS",      new NISAlgorithm()      },
                { "MTSR",     new MTSRAlgorithm()     },
                { "Anime4K",  new Anime4KAlgorithm()  },
                { "Integer",  new IntegerAlgorithm()  },
                { "xBR",      new xBRAlgorithm()      }
            };
        }

        public IReadOnlyDictionary<string, IUpscalingAlgorithm> Algorithms => _algorithms;

        public IUpscalingAlgorithm Get(string name)
        {
            if (_algorithms.TryGetValue(name, out var algo)) return algo;
            return _algorithms["LS1"];
        }

        public IEnumerable<AlgorithmInfo> GetAll() =>
            _algorithms.Select(kv => new AlgorithmInfo
            {
                Name              = kv.Key,
                DisplayName       = kv.Value.Name,
                Description       = kv.Value.Description,
                Performance       = kv.Value.Performance,
                SupportsSharpness = kv.Value.SupportsSharpness
            });
    }
}
