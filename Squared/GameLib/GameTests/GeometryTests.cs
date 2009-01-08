﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;
using Squared.Game;
using Microsoft.Xna.Framework;

namespace Squared.Game {
    [TestFixture]
    public class GeometryTests {
        public Polygon MakeSquare (float x, float y, float size) {
            size /= 2;

            return new Polygon(new Vector2[] {
                new Vector2(x - size, y - size),
                new Vector2(x + size, y - size),
                new Vector2(x + size, y + size),
                new Vector2(x - size, y + size)
            });
        }

        [Test]
        public void ProjectOntoAxisTest () {
            var vertices = MakeSquare(0, 0, 5);

            var expected = new Interval(-2.5f, 2.5f);
            var interval = Geometry.ProjectOntoAxis(new Vector2(0.0f, 1.0f), vertices);

            Assert.AreEqual(expected, interval);

            expected = new Interval(-2.5f, 2.5f);
            interval = Geometry.ProjectOntoAxis(new Vector2(1.0f, 0.0f), vertices);

            Assert.AreEqual(expected, interval);

            var distance = (vertices[2] - vertices[0]).Length();
            expected = new Interval(-distance / 2.0f, distance / 2.0f);
            var vec = new Vector2(1.0f, 1.0f);
            vec.Normalize();
            interval = Geometry.ProjectOntoAxis(vec, vertices);

            Assert.AreEqual(expected, interval);
        }

        [Test]
        public void DoPolygonsIntersectTest () {
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(0, 0, 5)));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(2, 0, 5)));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(2, 2, 5)));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(2, 2, 5), MakeSquare(2, 0, 5)));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(2, 2, 5), MakeSquare(0, 0, 5)));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(1.9f, 1.9f, 2), MakeSquare(0, 0, 2)));

            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(2, 2, 2), MakeSquare(0, 0, 2)));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(3, 3, 5), MakeSquare(-3, -3, 5)));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(3, 3, 5), MakeSquare(-3, 0, 5)));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(3, 3, 5), MakeSquare(0, -3, 5)));
        }

        [Test]
        public void ResolvePolygonMotionTest () {
            var result = Geometry.ResolvePolygonMotion(MakeSquare(0, 0, 5), MakeSquare(0, 0, 5), new Vector2(0, 0));
            Assert.IsTrue(result.AreIntersecting);
            Assert.IsTrue(result.WouldHaveIntersected);
            Assert.IsTrue(result.WillBeIntersecting);

            result = Geometry.ResolvePolygonMotion(MakeSquare(0, 0, 5), MakeSquare(5.1f, 0, 5), new Vector2(-5, 0));
            Assert.IsFalse(result.AreIntersecting);
            Assert.IsFalse(result.WouldHaveIntersected);
            Assert.IsFalse(result.WillBeIntersecting);

            Assert.AreEqual(result.ResultVelocity, new Vector2(-5, 0));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(result.ResultVelocity.X, result.ResultVelocity.Y, 5), MakeSquare(5.1f, 0, 5)));

            result = Geometry.ResolvePolygonMotion(MakeSquare(0, 0, 5), MakeSquare(5.1f, 0, 5), new Vector2(5, 0));
            Assert.IsFalse(result.AreIntersecting);
            Assert.IsTrue(result.WouldHaveIntersected);
            Assert.IsFalse(result.WillBeIntersecting);

            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(result.ResultVelocity.X, result.ResultVelocity.Y, 5), MakeSquare(5.1f, 0, 5)));

            result = Geometry.ResolvePolygonMotion(MakeSquare(0, 0, 5), MakeSquare(5.1f, 5.1f, 5), new Vector2(5, 5));
            Assert.IsFalse(result.AreIntersecting);
            Assert.IsTrue(result.WouldHaveIntersected);
            Assert.IsFalse(result.WillBeIntersecting);

            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(result.ResultVelocity.X, result.ResultVelocity.Y, 5), MakeSquare(5.1f, 5.1f, 5)));
        }

        [Test]
        public void PointInTriangleTest () {
            var tri = new Vector2[] { 
                new Vector2(0.0f, 0.0f),
                new Vector2(2.0f, 0.0f),
                new Vector2(0.0f, 2.0f)
            };

            Assert.IsTrue(Geometry.PointInTriangle(
                new Vector2(0.5f, 0.5f), tri
            ));

            Assert.IsFalse(Geometry.PointInTriangle(
                tri[0], tri
            ));
            Assert.IsFalse(Geometry.PointInTriangle(
                tri[1], tri
            ));
            Assert.IsFalse(Geometry.PointInTriangle(
                tri[2], tri
            ));

            Assert.IsFalse(Geometry.PointInTriangle(
                new Vector2(-1.0f, -1.0f), tri
            ));
            Assert.IsFalse(Geometry.PointInTriangle(
                new Vector2(-1.0f, 2.0f), tri
            ));
            Assert.IsFalse(Geometry.PointInTriangle(
                new Vector2(2.0f, -1.0f), tri
            ));
            Assert.IsFalse(Geometry.PointInTriangle(
                new Vector2(2.0f, 2.0f), tri
            ));
        }

        [Test]
        public void TriangulateTest () {
            var square = new Vector2[] {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.0f, 1.0f)
            };

            var triangles = Geometry.Triangulate(square).ToArray();
            Assert.AreEqual(2, triangles.Length);
            Assert.AreEqual(3, triangles[0].Length);
            Assert.AreEqual(3, triangles[1].Length);
            Assert.AreEqual(square[3], triangles[0][0]);
            Assert.AreEqual(square[0], triangles[0][1]);
            Assert.AreEqual(square[1], triangles[0][2]);
            Assert.AreEqual(square[1], triangles[1][0]);
            Assert.AreEqual(square[2], triangles[1][1]);
            Assert.AreEqual(square[3], triangles[1][2]);
        }
    }
}