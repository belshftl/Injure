// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Injure.Common;

namespace Injure.Gpu;

/// <summary>
/// Frame-local command recording scope: a <see cref="GpuCommandEncoder"/> plus, optionally, the
/// <see cref="IAcquiredOutput"/> the frame is presented on.
/// </summary>
/// <remarks>
/// <para>
/// Intended to be used as a scope via <c>using</c>. <see cref="Submit()"/> finishes and submits the
/// recorded commands and presents the primary output; if a frame is disposed without being
/// submitted, the recorded work is discarded and the primary output is released without being
/// presented.
/// </para>
/// <para>
/// A frame without a primary output is useful for offscreen-only rendering, e.g. headless
/// rendering or rendering to textures before the first window frame.
/// </para>
/// <para>
/// Temporary resources that must survive up to the submission (or discard via disposal) can be
/// registered with <see cref="AddDisp{T}(T)"/>.
/// </para>
/// </remarks>
public sealed class RenderFrame : IDisposable, IDisposalScope {
	private readonly GpuDevice device;
	private readonly GpuCommandEncoder encoder;
	private readonly IAcquiredOutput? primaryOutput;
	private readonly List<IDisposable> deferred = new();
	private bool done = false;

	/// <summary>
	/// Begins a frame, taking ownership of <paramref name="primaryOutput"/> (<b>including if this
	/// constructor throws</b>).
	/// </summary>
	/// <param name="device">Device to record and submit the frame's commands on.</param>
	/// <param name="primaryOutput">
	/// The output to render to and present on, or <see langword="null"/> for none.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> is <see langword="null"/>.
	/// </exception>
	public RenderFrame(GpuDevice device, IAcquiredOutput? primaryOutput) {
		try {
			ArgumentNullException.ThrowIfNull(device);
			encoder = device.CreateCommandEncoder();
			Encoder = encoder.AsRef();
		} catch {
			primaryOutput?.Dispose();
			throw;
		}
		this.device = device;
		this.primaryOutput = primaryOutput;
	}

	/// <summary>
	/// Acquires an image from <paramref name="output"/> and begins a frame presenting on it.
	/// </summary>
	/// <param name="device">Device to record and submit the frame's commands on.</param>
	/// <param name="output">Output to acquire the frame's primary output from.</param>
	/// <param name="frame">On success, the new frame.</param>
	/// <returns>
	/// <see langword="true"/> if a frame was begun; <see langword="false"/> if
	/// <see cref="IRenderOutput.TryAcquire(out IAcquiredOutput)"/> returned <see langword="false"/>,
	/// i.e. this frame should be skipped.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> or <paramref name="output"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryBegin(GpuDevice device, IRenderOutput output, [NotNullWhen(true)] out RenderFrame? frame) {
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(output);
		if (!output.TryAcquire(out IAcquiredOutput? acquired)) {
			frame = null;
			return false;
		}
		frame = new RenderFrame(device, acquired);
		return true;
	}

	/// <summary>
	/// The device this frame records and submits its commands on.
	/// </summary>
	public GpuDevice Device => device;

	/// <summary>
	/// The encoder that records this frame's commands.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted or disposed.
	/// </exception>
	public GpuCommandEncoderRef Encoder => done
		? throw new InvalidOperationException("frame already submitted/disposed")
		: field;

	/// <summary>
	/// Whether this frame has a primary output.
	/// </summary>
	public bool HasPrimaryOutput => primaryOutput is not null;

	/// <summary>
	/// The color view of this frame's primary output.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has no primary output, or has already been submitted or disposed.
	/// </exception>
	/// <remarks>
	/// The primary output is color-only. Passes that require depth/stencil should use an offscreen
	/// render target instead.
	/// </remarks>
	public GpuTextureViewRef PrimaryView {
		get {
			if (done)
				throw new InvalidOperationException("frame already submitted/disposed");
			return (primaryOutput ?? throw new InvalidOperationException("frame has no primary output")).View;
		}
	}

	/// <summary>
	/// Convenience method to open a render pass targeting the primary output, equivalent to
	/// <see cref="GpuCommandEncoderHandle.BeginColorPass(GpuTextureViewHandle, in ColorAttachmentOps)"/>
	/// on <see cref="Encoder"/> with <see cref="PrimaryView"/>.
	/// </summary>
	/// <inheritdoc cref="GpuCommandEncoderHandle.BeginColorPass(GpuTextureViewHandle, in ColorAttachmentOps)"/>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has no primary output, or has already been submitted or disposed, or if
	/// a render pass is already active.
	/// </exception>
	public RenderPass BeginPrimaryPass(in ColorAttachmentOps colorOps) =>
		Encoder.BeginColorPass(PrimaryView, in colorOps);

	/// <summary>
	/// Registers an <see cref="IDisposable"/> for cleanup after this frame is submitted or discarded.
	/// </summary>
	/// <param name="disposable">Object to dispose once the frame is finished.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disposable"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Typically used for temporary resources that are no longer needed by CPU code after command
	/// recording, but must remain alive until the submit is complete because encoded GPU work
	/// references them.
	/// </para>
	/// <para>
	/// Registered objects are disposed in reverse registration order, after the primary output. See
	/// <see cref="Submit()"/> for how failures are reported.
	/// </para>
	/// </remarks>
	public T AddDisp<T>(T disposable) where T : notnull, IDisposable {
		ArgumentNullException.ThrowIfNull(disposable);
		if (done)
			throw new InvalidOperationException("frame already submitted/disposed");
		deferred.Add(disposable);
		return disposable;
	}

	/// <summary>
	/// Finishes command recording, submits the frame, and presents the primary output if there is
	/// one.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed, or if a render pass is still active.
	/// </exception>
	/// <exception cref="AggregateException">
	/// Thrown if more than one failure happened while ending the frame; see the remarks.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Calls <see cref="IAcquiredOutput.RecordFinalCommands(GpuCommandEncoderHandle)"/> on the primary
	/// output before finishing the encoder, then submits, then presents. Afterwards, the primary
	/// output and the disposables registered with <see cref="AddDisp{T}(T)"/> are disposed.
	/// </para>
	/// <para>
	/// The two <see cref="InvalidOperationException"/> cases above leave the frame as it was. Past
	/// them, the frame always ends: if recording the final commands, finishing, submitting, or
	/// presenting throws, the rest of those steps are skipped, but the primary output and the
	/// registered disposables are still disposed. Every disposal runs even if an earlier one throws.
	/// A single failure is rethrown as-is; several are thrown together as an
	/// <see cref="AggregateException"/>, with the failure that ended the frame first.
	/// </para>
	/// </remarks>
	public void Submit() {
		if (done)
			throw new InvalidOperationException("frame already submitted/disposed");
		if (encoder.State.ActivePass)
			throw new InvalidOperationException("frame still has an active render pass");
		Exception? failure = null;
		try {
			primaryOutput?.RecordFinalCommands(Encoder);
			using GpuCommandBuffer cmdbuf = encoder.Finish();
			device.Submit(cmdbuf);
			primaryOutput?.Present();
		} catch (Exception e) {
			failure = e;
		}
		end(failure);
	}

	/// <summary>
	/// Discards the frame if it has not already been submitted.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if a render pass is still active.
	/// </exception>
	/// <exception cref="AggregateException">
	/// Thrown if more than one disposal failed; a single failure is rethrown as-is.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Callers should structure usage such that this always runs, typically via <c>using</c>, rather
	/// than only disposing on non-submit paths.
	/// </para>
	/// <para>
	/// The primary output is released without being presented, and any disposables registered with
	/// <see cref="AddDisp{T}(T)"/> are disposed, even if an earlier disposal throws. Disposing a frame
	/// with an active pass is a bug and throws instead of silently ending the pass; the frame is left
	/// as it was then.
	/// </para>
	/// </remarks>
	public void Dispose() {
		if (done)
			return;
		if (encoder.State.ActivePass)
			throw new InvalidOperationException("frame still has an active render pass");
		end(null);
	}

	// releases everything the frame owns, then throws `failure` along with any disposal failures
	private void end(Exception? failure) {
		done = true;
		List<Exception>? failures = failure is null ? null : [failure];
		// no-op if Finish already ran. RecordFinalCommands throwing mid-pass is the only way to get
		// here with an active pass; the encoder can't be released then, so it's left to leak
		if (!encoder.State.ActivePass)
			run(encoder.Dispose, ref failures);
		if (primaryOutput is not null)
			run(primaryOutput.Dispose, ref failures);
		// reverse registration order, as IDisposalScope promises
		for (int i = deferred.Count - 1; i >= 0; i--)
			run(deferred[i].Dispose, ref failures);
		deferred.Clear();

		if (failures is null)
			return;
		if (failures.Count == 1)
			ExceptionDispatchInfo.Throw(failures[0]);
		throw new AggregateException(failures);

		static void run(Action dispose, ref List<Exception>? failures) {
			try {
				dispose();
			} catch (Exception e) {
				(failures ??= []).Add(e);
			}
		}
	}
}
