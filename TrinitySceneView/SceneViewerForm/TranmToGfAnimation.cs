using System.Collections.Generic;
using FlatSharp;
using GfAnim = Trinity.Core.Flatbuffers.GF.Animation;
using TrAnim = Trinity.Core.Flatbuffers.TR.Animation;

namespace TrinitySceneView
{
    internal static class TranmToGfAnimation
    {
        internal static GfAnim.Animation Convert(TrAnim.TRANM tranm)
        {
            if (tranm?.Info == null || tranm.SkeletalAnimation == null)
            {
                return new GfAnim.Animation();
            }

            var anim = new GfAnim.Animation
            {
                Info = new GfAnim.Info
                {
                    DoesLoop = tranm.Info.PlayType == TrAnim.PlayType.Looped ? 1u : 0u,
                    KeyFrames = tranm.Info.FrameCount,
                    FrameRate = tranm.Info.FrameRate,
                },
                Skeleton = new GfAnim.BoneAnimation
                {
                    Tracks = new List<GfAnim.BoneTrack>()
                }
            };

            var tracks = anim.Skeleton.Tracks;
            if (tranm.SkeletalAnimation.Tracks != null)
            {
                foreach (var t in tranm.SkeletalAnimation.Tracks)
                {
                    if (t == null)
                    {
                        continue;
                    }

                    tracks.Add(new GfAnim.BoneTrack
                    {
                        Name = t.BoneName ?? string.Empty,
                        Scale = ConvertVectorTrack(t.ScaleChannel),
                        Rotate = ConvertRotationTrack(t.RotationChannel),
                        Translate = ConvertVectorTrack(t.TranslateChannel),
                    });
                }
            }

            return anim;
        }

        private static FlatBufferUnion<GfAnim.FixedVectorTrack, GfAnim.DynamicVectorTrack, GfAnim.Framed16VectorTrack, GfAnim.Framed8VectorTrack>
            ConvertVectorTrack(FlatBufferUnion<TrAnim.FixedVectorTrack, TrAnim.FramedVectorTrack, TrAnim.Keyed16VectorTrack, TrAnim.Keyed8VectorTrack> track)
        {
            FlatBufferUnion<GfAnim.FixedVectorTrack, GfAnim.DynamicVectorTrack, GfAnim.Framed16VectorTrack, GfAnim.Framed8VectorTrack> result = new();

            track.Switch(
                defaultCase: () => result = new(),
                case1: v =>
                {
                    result = v == null
                        ? new()
                        : new(new GfAnim.FixedVectorTrack { Co = v.Value });
                },
                case2: v =>
                {
                    result = v?.Values == null
                        ? new()
                        : new(new GfAnim.DynamicVectorTrack { Co = v.Values });
                },
                case3: v =>
                {
                    result = v?.Values == null || v.Keys == null
                        ? new()
                        : new(new GfAnim.Framed16VectorTrack { Frames = v.Keys, Co = v.Values });
                },
                case4: v =>
                {
                    result = v?.Values == null || v.Keys == null
                        ? new()
                        : new(new GfAnim.Framed8VectorTrack { Frames = v.Keys, Co = v.Values });
                }
            );

            return result;
        }

        private static FlatBufferUnion<GfAnim.FixedRotationTrack, GfAnim.DynamicRotationTrack, GfAnim.Framed16RotationTrack, GfAnim.Framed8RotationTrack>
            ConvertRotationTrack(FlatBufferUnion<TrAnim.FixedRotationTrack, TrAnim.FramedRotationTrack, TrAnim.Keyed16RotationTrack, TrAnim.Keyed8RotationTrack> track)
        {
            FlatBufferUnion<GfAnim.FixedRotationTrack, GfAnim.DynamicRotationTrack, GfAnim.Framed16RotationTrack, GfAnim.Framed8RotationTrack> result = new();

            track.Switch(
                defaultCase: () => result = new(),
                case1: v =>
                {
                    result = v == null
                        ? new()
                        : new(new GfAnim.FixedRotationTrack { Co = v.Value });
                },
                case2: v =>
                {
                    result = v?.Values == null
                        ? new()
                        : new(new GfAnim.DynamicRotationTrack { Co = v.Values });
                },
                case3: v =>
                {
                    result = v?.Values == null || v.Keys == null
                        ? new()
                        : new(new GfAnim.Framed16RotationTrack { Frames = v.Keys, Co = v.Values });
                },
                case4: v =>
                {
                    result = v?.Values == null || v.Keys == null
                        ? new()
                        : new(new GfAnim.Framed8RotationTrack { Frames = v.Keys, Co = v.Values });
                }
            );

            return result;
        }
    }
}
