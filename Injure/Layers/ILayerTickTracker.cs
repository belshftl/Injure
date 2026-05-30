// SPDX-License-Identifier: MIT

using Injure.Timing;

namespace Injure.Layers;

public interface ILayerTickFeeder {
	T Feed<T>(T obj) where T : class, IMonoTickReceiver;
}
