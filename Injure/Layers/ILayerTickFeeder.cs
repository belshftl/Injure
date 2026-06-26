// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Time;

namespace Injure.Layers;

public interface ILayerTickFeeder {
	T Feed<T>(T obj) where T : class, IMonoTickReceiver;
}
