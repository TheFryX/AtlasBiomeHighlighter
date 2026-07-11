using System;
using System.Collections.Generic;
using System.Numerics;

namespace AtlasBiomeHighlighter
{
    public partial class AtlasBiomeHighlighter
    {
        private const float AtlasSignalOriginEpsilon = 0.75f;
        private const float AtlasSignalCollisionBucketSize = 6f;

        private readonly Dictionary<(int x, int y), AtlasSignalOriginBucket> _atlasSignalOriginBuckets = new(256);
        private readonly HashSet<(int x, int y)> _atlasSignalOriginCollisionBuckets = new();

        private struct AtlasSignalOriginBucket
        {
            public (int x, int y) FirstCoordinate;
            public bool HasCoordinate;
        }

        private void PrepareAtlasSignalOriginCollisionGuard()
        {
            _atlasSignalOriginBuckets.Clear();
            _atlasSignalOriginCollisionBuckets.Clear();

            for (int i = 0; i < _visibleNodeInfos.Count; i++)
            {
                var info = _visibleNodeInfos[i];
                try
                {
                    var element = info.Node?.Element;
                    if (element == null)
                        continue;

                    var position = element.Position;
                    if (Math.Abs(position.X) > AtlasSignalOriginEpsilon ||
                        Math.Abs(position.Y) > AtlasSignalOriginEpsilon)
                    {
                        continue;
                    }

                    var centerValue = element.Center;
                    var center = new Vector2(centerValue.X, centerValue.Y);
                    if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
                        continue;

                    var coordinateValue = info.Node.Coordinate;
                    var coordinate = (coordinateValue.X, coordinateValue.Y);
                    var bucketKey = GetAtlasSignalCollisionBucketKey(center);

                    if (!_atlasSignalOriginBuckets.TryGetValue(bucketKey, out var bucket))
                    {
                        _atlasSignalOriginBuckets[bucketKey] = new AtlasSignalOriginBucket
                        {
                            FirstCoordinate = coordinate,
                            HasCoordinate = true
                        };
                    }
                    else if (bucket.HasCoordinate && bucket.FirstCoordinate != coordinate)
                    {
                        _atlasSignalOriginCollisionBuckets.Add(bucketKey);
                    }
                }
                catch
                {
                    // A disappearing wrapper is ignored for this frame. This guard must not
                    // alter the normal Atlas render path or promote diagnostic failures.
                }
            }
        }

        private bool IsAtlasSignalOriginCollision(NodeRenderInfo info, Vector2 center)
        {
            try
            {
                var element = info.Node?.Element;
                if (element == null)
                    return false;

                var position = element.Position;
                if (Math.Abs(position.X) > AtlasSignalOriginEpsilon ||
                    Math.Abs(position.Y) > AtlasSignalOriginEpsilon)
                {
                    return false;
                }

                return _atlasSignalOriginCollisionBuckets.Contains(GetAtlasSignalCollisionBucketKey(center));
            }
            catch
            {
                return false;
            }
        }

        private static (int x, int y) GetAtlasSignalCollisionBucketKey(Vector2 center)
        {
            return (
                (int)MathF.Round(center.X / AtlasSignalCollisionBucketSize),
                (int)MathF.Round(center.Y / AtlasSignalCollisionBucketSize));
        }

        private static bool IsInvalidAtlasSignalPlaceholder(NodeRenderInfo info, Vector2 center)
        {
            try
            {
                var element = info.Node?.Element;
                if (element == null || element.ChildCount != 0)
                    return false;

                var position = element.Position;
                float width = element.Width;
                float height = element.Height;

                bool localOrigin =
                    Math.Abs(position.X) <= AtlasSignalOriginEpsilon &&
                    Math.Abs(position.Y) <= AtlasSignalOriginEpsilon;
                bool cornerCenter =
                    center.X >= 0f && center.X <= 320f &&
                    center.Y >= 0f && center.Y <= 300f;

                // Second captured signature: a childless wrapper can be expanded to 120x138
                // while still sitting at local origin. A legitimate origin-based 60x60 node in
                // the log had a real transformed center at 633,881, so constrain this guard to
                // the corner placeholder region instead of rejecting every origin-based node.
                if (element.ChildCount == 0 && localOrigin && cornerCenter)
                    return true;

                // Captured ExileCore failure signature:
                //   position=<0,0> or <60,0>, size=60x60, childCount=0,
                //   center=<23,23> or <69,23>, IsVisible=True.
                // Do not use IsVisible or map status here: placeholder wrappers report visible,
                // while legitimate locked nodes may report invisible.
                return float.IsFinite(center.X) &&
                       float.IsFinite(center.Y) &&
                       Math.Abs(position.Y) <= 0.75f &&
                       width >= 59f && width <= 61f &&
                       height >= 59f && height <= 61f &&
                       center.Y >= 0f && center.Y <= 40f;
            }
            catch
            {
                return false;
            }
        }
    }
}
