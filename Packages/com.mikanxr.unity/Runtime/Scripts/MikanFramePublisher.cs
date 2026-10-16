using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MikanXR.Unity
{
	// Pairs a rendered frame with its index on the wire once the GPU has finished with it. The
	// Spout senders capture at the end of the frame, so this runs its end-of-frame step after
	// theirs (a later execution order), drops a fence behind their copies, and publishes the
	// frame from a later Update once the fence has passed. Without fence support it publishes
	// on the next frame, which the sender's end-of-frame copy has preceded.
	//
	// A frame the end-of-frame step never saw (batch mode fires no WaitForEndOfFrame) is
	// published a frame late from Update instead, so a host without that callback still pairs
	// every frame, one frame behind.
	[DefaultExecutionOrder(1000)]
	public class MikanFramePublisher : MonoBehaviour
	{
		private struct PendingPublish
		{
			public int cameraId;
			public long frameIndex;
			public GraphicsFence fence;
			public bool hasFence;
			public int queuedFrameCount;
		}

		private readonly List<PendingPublish> _renderedThisFrame = new List<PendingPublish>();
		private readonly List<PendingPublish> _awaitingGpu = new List<PendingPublish>();
		private MikanClient _client;
		private long _publishCount = 0;

		public long PublishCount => _publishCount;

		private void Awake()
		{
			_client = GetComponent<MikanClient>();
		}

		private void OnEnable()
		{
			StartCoroutine(EndOfFrameLoop());
		}

		// Called by a camera object right after it renders a frame
		public void QueuePublish(int cameraId, long frameIndex)
		{
			_renderedThisFrame.Add(new PendingPublish()
			{
				cameraId = cameraId,
				frameIndex = frameIndex,
				queuedFrameCount = Time.frameCount
			});
		}

		private IEnumerator EndOfFrameLoop()
		{
			var endOfFrame = new WaitForEndOfFrame();

			while (true)
			{
				yield return endOfFrame;

				for (int index = 0; index < _renderedThisFrame.Count; ++index)
				{
					PendingPublish pending = _renderedThisFrame[index];

					if (SystemInfo.supportsGraphicsFence)
					{
						pending.fence = Graphics.CreateGraphicsFence(
							GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);
						pending.hasFence = true;
					}

					_awaitingGpu.Add(pending);
				}

				_renderedThisFrame.Clear();
			}
		}

		private void Update()
		{
			if (_client == null || !_client.IsConnected)
			{
				_renderedThisFrame.Clear();
				_awaitingGpu.Clear();
				return;
			}

			// Frames left over from an earlier frame missed the end-of-frame step
			for (int index = 0; index < _renderedThisFrame.Count;)
			{
				PendingPublish pending = _renderedThisFrame[index];
				if (pending.queuedFrameCount < Time.frameCount)
				{
					_renderedThisFrame.RemoveAt(index);
					_awaitingGpu.Add(pending);
				}
				else
				{
					++index;
				}
			}

			for (int index = 0; index < _awaitingGpu.Count;)
			{
				PendingPublish pending = _awaitingGpu[index];
				bool ready = pending.hasFence ? pending.fence.passed : Time.frameCount > pending.queuedFrameCount;

				if (!ready)
				{
					++index;
					continue;
				}

				_awaitingGpu.RemoveAt(index);
				_client.SendRequest(new PublishCameraRenderTargetTextures()
				{
					camera_id = pending.cameraId,
					frame_index = pending.frameIndex
				});
				++_publishCount;
			}
		}
	}
}
