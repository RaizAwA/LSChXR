using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;


public class AnimatorManagerTest
{
    // Objetos creados en cada test para limpiarlos después.
    readonly List<Object> _creados = new List<Object>();

    // Crea un AnimatorManager con el alfabeto indicado.
    // No inyecta 'tmp': la mayoría de los tests no tocan ChangeText().
    // Para el caso que sí lo necesita, usar InyectarTmp().
    AnimatorManager Crear(params (string key, string value)[] alfabeto)
    {
        var go = new GameObject("AnimatorManager");
        _creados.Add(go);
        var am = go.AddComponent<AnimatorManager>();

        foreach (var (key, value) in alfabeto)
        {
            am.alphabet.Add(new AnimatorManager.Dict { key = key, value = value });
        }

        return am;
    }

    // Inyecta un TextMeshPro real en el campo privado 'tmp' (vía reflexión)
    // para que ChangeText() no lance NullReferenceException.
    void InyectarTmp(AnimatorManager am)
    {
        var tmpGo = new GameObject("tmp");
        _creados.Add(tmpGo);
        var tmp = tmpGo.AddComponent<TextMeshPro>();

        typeof(AnimatorManager)
            .GetField("tmp", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(am, tmp);
    }

    [TearDown]
    public void Limpiar()
    {
        foreach (var obj in _creados)
        {
            if (obj != null) Object.DestroyImmediate(obj);
        }
        _creados.Clear();
    }

    [Test]
    public void GetTriggerAnim_ReciénCreado_EsFalse()
    {
        var am = Crear();
        Assert.IsFalse(am.GetTriggerAnim());
    }

    [Test]
    public void Interpret_PalabraSimple_LlenaFifosEnOrden()
    {
        var am = Crear(("A", "anim_A"), ("B", "anim_B"), ("O", "anim_O"));

        am.Interpret("abo");

        CollectionAssert.AreEqual(new[] { "A", "B", "O" }, am.wordFifo);
        CollectionAssert.AreEqual(new[] { "anim_A", "anim_B", "anim_O" }, am.animFifo);
        Assert.IsTrue(am.GetTriggerAnim(), "Con animaciones en cola, triggerAnim debe activarse.");
    }

    [Test]
    public void Interpret_DobleL_UsaAnimacionLL_YCuentaUnaSolaVez()
    {
        var am = Crear(("L", "anim_L"), ("LL", "anim_LL"));

        am.Interpret("ll");

        CollectionAssert.AreEqual(new[] { "LL" }, am.wordFifo);
        CollectionAssert.AreEqual(new[] { "anim_LL" }, am.animFifo);
    }

    [Test]
    public void Interpret_DobleR_UsaAnimacionRR()
    {
        var am = Crear(("R", "anim_R"), ("RR", "anim_RR"));

        am.Interpret("rr");

        CollectionAssert.AreEqual(new[] { "RR" }, am.wordFifo);
        CollectionAssert.AreEqual(new[] { "anim_RR" }, am.animFifo);
    }

    [Test]
    public void Interpret_LSimple_NoSeConfundeConLL()
    {
        var am = Crear(("L", "anim_L"), ("LL", "anim_LL"), ("O", "anim_O"));

        am.Interpret("lo");

        CollectionAssert.AreEqual(new[] { "L", "O" }, am.wordFifo);
        CollectionAssert.AreEqual(new[] { "anim_L", "anim_O" }, am.animFifo);
    }

    [Test]
    public void Interpret_MezclaLLyLetras_MantieneOrdenYSaltos()
    {
        var am = Crear(("L", "anim_L"), ("LL", "anim_LL"), ("A", "anim_A"), ("O", "anim_O"));

        am.Interpret("llao");

        CollectionAssert.AreEqual(new[] { "LL", "A", "O" }, am.wordFifo);
        CollectionAssert.AreEqual(new[] { "anim_LL", "anim_A", "anim_O" }, am.animFifo);
    }

    [Test]
    public void Interpret_TildesYDieresis_SeNormalizan()
    {
        var am = Crear(("A", "a"), ("E", "e"), ("I", "i"), ("O", "o"), ("U", "u"));

        am.Interpret("áÉíÓü"); // ü también debe mapear a U

        CollectionAssert.AreEqual(new[] { "A", "E", "I", "O", "U" }, am.wordFifo);
        CollectionAssert.AreEqual(new[] { "a", "e", "i", "o", "u" }, am.animFifo);
    }

    [Test]
    public void Interpret_CaracterNoAlfabetico_SeDescarta()
    {
        var am = Crear(("A", "anim_A"));

        // Un dígito no es una seña válida (p.ej. si el OCR cuela un número):
        // debe saltarse sin romper la interpretación de las letras.
        am.Interpret("a1a");

        CollectionAssert.AreEqual(new[] { "A", "A" }, am.wordFifo);
        CollectionAssert.AreEqual(new[] { "anim_A", "anim_A" }, am.animFifo);
    }

    [Test]
    public void Interpret_SinCoincidencias_NoDisparaYDejaFifosVacios()
    {
        var am = Crear(); // alfabeto vacío -> nada coincide
        InyectarTmp(am);  // este path llama a ChangeText("")

        am.Interpret("123");

        Assert.IsEmpty(am.wordFifo);
        Assert.IsEmpty(am.animFifo);
        Assert.IsFalse(am.GetTriggerAnim(), "Sin animaciones no debe activarse triggerAnim.");
    }

    [Test]
    public void MoveTo_SeUbicaMedioMetroDelanteDelObjetivo()
    {
        var am = Crear();

        var target = new GameObject("target").transform;
        _creados.Add(target.gameObject);
        target.position = new Vector3(1f, 2f, 3f);
        target.rotation = Quaternion.Euler(0f, 90f, 0f);

        am.MoveTo(target);

        Vector3 esperado = target.position + target.forward * 0.5f;
        Assert.Less(Vector3.Distance(am.transform.position, esperado), 1e-4f);
        Assert.Less(Quaternion.Angle(am.transform.rotation, target.rotation), 1e-3f);
    }
}
